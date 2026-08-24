using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 管理者画面のトップレベル View。UIDocument の要素を組み立て、
/// 接続・Broadcast を <see cref="TimelineHubClient"/>（<see cref="ITimelinePublisher"/>）に橋渡しする。
/// タイムライン行の UI 構築・バインドは <see cref="TimelineListController"/> の責務とし、ここでは持たない。
/// 見た目（レイアウト・配色）は UXML / USS 側の責務とし、ここでは持たない。
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AdminConsoleView : MonoBehaviour
{
    [SerializeField] private ServerConfig _serverConfig;
    [SerializeField] private string _roomId = "room1";
    [SerializeField] private StyleSheet _styleSheet;

    // 行・フッターの構造は TimelineRow.uxml / TimelineFooter.uxml 側で定義する。
    // ここでは TimelineListController に渡すためだけに参照を持つ。
    [SerializeField] private VisualTreeAsset _timelineRowTemplate;
    [SerializeField] private VisualTreeAsset _timelineFooterTemplate;

    private readonly List<TimelineRowModel> _rows = new();

    private ITimelinePublisher _client;
    private TimelineListController _timelineController;
    private ConnectionState _state = ConnectionState.Disconnected;
    private BroadcastState _broadcastState = BroadcastState.Idle;
    private BroadcastSession _session;

    private TextField _serverAddressField;
    private TextField _roomIdField;
    private Button _connectButton;
    private Label _connectionStatusLabel;
    private VisualElement _statusDot;
    private VisualElement _statusDotGlow;
    private ListView _timelineList;
    private Button _broadcastButton;
    private Button _stopButton;
    private Label _statusLabel;

    /// <summary>
    /// UIDocument の要素を Query して初期値を設定し、TimelineListController のセットアップと
    /// ボタンのイベント購読を行う。UIDocument が有効化されるたびに rootVisualElement が
    /// 再構築されるため、Query・購読は Awake ではなくここで毎回やり直す。
    /// </summary>
    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        // UXML の <Style src="..."> は相対パスでは解決されないため、コード側で取り付ける。
        if (_styleSheet != null)
            root.styleSheets.Add(_styleSheet);
        else
            Debug.LogWarning("[AdminConsoleView] _styleSheet が未設定です。");

        _serverAddressField = root.Q<TextField>("server-address");
        _roomIdField = root.Q<TextField>("room-id");
        _connectButton = root.Q<Button>("connect-button");
        _connectionStatusLabel = root.Q<Label>("connection-status");
        _statusDot = root.Q<VisualElement>("status-dot");
        _statusDotGlow = root.Q<VisualElement>("status-dot-glow");
        _timelineList = root.Q<ListView>("timeline-list");
        _broadcastButton = root.Q<Button>("broadcast-button");
        _stopButton = root.Q<Button>("stop-button");
        _statusLabel = root.Q<Label>("status-label");

        _serverAddressField.value = _serverConfig != null ? _serverConfig.ServerAddress : string.Empty;
        _roomIdField.value = _roomId;

        if (_rows.Count == 0)
            _rows.Add(new TimelineRowModel());

        _timelineController = new TimelineListController(_timelineList, _rows, _timelineRowTemplate, _timelineFooterTemplate);
        _timelineController.Setup();

        _connectButton.clicked += OnConnectClicked;
        _broadcastButton.clicked += OnBroadcastClicked;
        _stopButton.clicked += OnStopClicked;

        // SetState(Disconnected) は Connected 以外への遷移として配信状態も Idle へ戻す。
        SetState(ConnectionState.Disconnected);
        SetStatus(string.Empty);
    }

    /// <summary>OnEnable で購読したイベントを解除する。再有効化時に OnEnable が再購読するため、多重購読を防ぐ。</summary>
    private void OnDisable()
    {
        if (_connectButton != null)
            _connectButton.clicked -= OnConnectClicked;
        if (_broadcastButton != null)
            _broadcastButton.clicked -= OnBroadcastClicked;
        if (_stopButton != null)
            _stopButton.clicked -= OnStopClicked;
    }

    /// <summary>
    /// 配信中（<see cref="BroadcastState.Broadcasting"/>）の間、進行状況を毎フレーム反映する。
    /// 基準時刻はこのクライアントの UtcNow であり、Server が打つ broadcastAtMs とは別クロックのため、
    /// あくまで Admin 画面の表示用の目安（受信側の実発火タイミングを保証するものではない）。
    /// </summary>
    private void Update()
    {
        if (_broadcastState != BroadcastState.Broadcasting || _session == null)
            return;

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (_session.IsFinished(nowMs))
        {
            SetBroadcastState(BroadcastState.Idle);
            SetStatus("配信完了");
            return;
        }

        _timelineController.SetActiveIndex(_session.ActiveIndex(nowMs));
        SetStatus($"配信中 {FormatSeconds(_session.ElapsedSeconds(nowMs))} / {FormatSeconds(_session.TotalSeconds)}");
    }

    private static string FormatSeconds(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalMinutes:D2}:{span.Seconds:D2}";
    }

    /// <summary>破棄時に接続中の Hub クライアントを DisposeAsync で確実に切断する。</summary>
    private async void OnDestroy()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    /// <summary>
    /// 接続ボタン押下時の処理。接続済みなら切断、それ以外は接続を試みる。
    /// </summary>
    private async void OnConnectClicked()
    {
        if (_state == ConnectionState.Connected)
        {
            await DisconnectAsync();
            return;
        }

        await ConnectAsync();
    }

    /// <summary>
    /// サーバへ接続する。再接続時に古い接続が残らないよう、既存クライアントがあれば
    /// 先に DisposeAsync してから新規接続する。接続に失敗した場合は確立しかけたクライアントも
    /// DisposeAsync で破棄し、Broadcast の null ガードが正しく効く状態に戻す。
    /// </summary>
    private async Task ConnectAsync()
    {
        var serverAddress = _serverAddressField.value?.Trim();
        var roomId = _roomIdField.value?.Trim();

        if (string.IsNullOrEmpty(serverAddress))
        {
            SetStatus("ServerAddress を入力してください。");
            return;
        }

        if (string.IsNullOrEmpty(roomId))
        {
            SetStatus("roomID を入力してください。");
            return;
        }

        SetState(ConnectionState.Connecting);
        SetStatus(string.Empty);

        try
        {
            if (_client != null)
                await _client.DisposeAsync();

            var hubClient = new TimelineHubClient();
            _client = hubClient;
            await hubClient.ConnectAsync(serverAddress, roomId);

            SetState(ConnectionState.Connected, $"接続済み（room: {roomId}）");
        }
        catch (Exception e)
        {
            if (_client != null)
            {
                await _client.DisposeAsync();
                _client = null;
            }

            SetState(ConnectionState.Failed, "接続失敗");
            SetStatus(e.Message);
            Debug.LogException(e);
        }
    }

    /// <summary>サーバから切断する。切断処理中の連打を防ぐため、完了までボタンを無効化する。</summary>
    private async Task DisconnectAsync()
    {
        _connectButton.SetEnabled(false);

        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        SetState(ConnectionState.Disconnected, "Not Connected");
        SetStatus(string.Empty);
    }

    /// <summary>
    /// Broadcast ボタン押下時の処理。<see cref="TimelineDraft.TryBuild"/> で行データを検証・変換してから送信する。
    /// 送信成功後は <see cref="BroadcastSession"/> を組み、進行状況の表示を開始する。
    /// </summary>
    private async void OnBroadcastClicked()
    {
        if (_client == null)
        {
            SetStatus("先に接続してください。");
            return;
        }

        if (!TimelineDraft.TryBuild(_rows, out var actions, out var error))
        {
            SetStatus(error);
            return;
        }

        _broadcastButton.SetEnabled(false);
        SetStatus("送信中...");

        try
        {
            await _client.BroadcastAsync(actions);

            // 表示用の目安として、送信直後のこのクライアントの時刻を基準にする。
            // Server が全受信者へ打つ broadcastAtMs とは別クロックのため、実発火タイミングの保証ではない。
            var broadcastAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _session = new BroadcastSession(actions, broadcastAtMs);
            SetBroadcastState(BroadcastState.Broadcasting);
        }
        catch (Exception e)
        {
            SetStatus($"送信に失敗しました: {e.Message}");
            Debug.LogException(e);
            UpdateActionButtons();
        }
    }

    /// <summary>
    /// STOP ボタン押下時の処理。<see cref="TimelineDraft.BuildStopActions"/> の単発タイムライン
    /// （play: false）を配信することで、受信側の進行中スケジュールを破棄させたうえで停止させる。
    /// </summary>
    private async void OnStopClicked()
    {
        if (_client == null)
            return;

        _stopButton.SetEnabled(false);

        try
        {
            await _client.BroadcastAsync(TimelineDraft.BuildStopActions());
            SetBroadcastState(BroadcastState.Idle);
            SetStatus("配信を停止しました。");
        }
        catch (Exception e)
        {
            SetStatus($"停止に失敗しました: {e.Message}");
            Debug.LogException(e);
            UpdateActionButtons();
        }
    }

    /// <summary>
    /// 接続状態を切り替える。CONNECT/DISCONNECT ボタンの表示・enabled、
    /// connection-status ラベルと status-dot の見た目をここに集約する。
    /// Connected 以外へ遷移するときは配信状態も Idle へ戻す（接続が切れた配信は続けられないため）。
    /// </summary>
    /// <param name="state">遷移先の状態。</param>
    /// <param name="connectionStatusText">connection-status ラベルの文言。省略時は状態ごとの既定文言を使う。</param>
    private void SetState(ConnectionState state, string connectionStatusText = null)
    {
        _state = state;

        _connectButton.text = state switch
        {
            ConnectionState.Connecting => "CONNECTING...",
            ConnectionState.Connected => "DISCONNECT",
            _ => "CONNECT",
        };
        _connectButton.SetEnabled(state != ConnectionState.Connecting);

        connectionStatusText ??= state switch
        {
            ConnectionState.Connecting => "接続中...",
            ConnectionState.Failed => "接続失敗",
            _ => "Not Connected",
        };
        _connectionStatusLabel.text = connectionStatusText;
        _connectionStatusLabel.EnableInClassList("status-live", state == ConnectionState.Connected);
        _connectionStatusLabel.EnableInClassList("status-error", state == ConnectionState.Failed);

        var isOffline = state != ConnectionState.Connected;
        _statusDot?.EnableInClassList("status-dot--offline", isOffline && state != ConnectionState.Failed);
        _statusDot?.EnableInClassList("status-dot--error", state == ConnectionState.Failed);
        _statusDotGlow?.EnableInClassList("status-dot-glow--offline", isOffline && state != ConnectionState.Failed);
        _statusDotGlow?.EnableInClassList("status-dot-glow--error", state == ConnectionState.Failed);

        if (state != ConnectionState.Connected)
            SetBroadcastState(BroadcastState.Idle);
        else
            UpdateActionButtons();
    }

    /// <summary>
    /// 配信状態を切り替える。Idle に戻るときはセッションを破棄し、行のハイライトも消す。
    /// status-label の配信中表示（status-live）と Broadcast/STOP ボタンの enabled をここに集約する。
    /// </summary>
    private void SetBroadcastState(BroadcastState state)
    {
        _broadcastState = state;

        if (state == BroadcastState.Idle)
        {
            _session = null;
            _timelineController?.SetActiveIndex(-1);
        }

        _statusLabel.EnableInClassList("status-live", state == BroadcastState.Broadcasting);

        UpdateActionButtons();
    }

    /// <summary>Broadcast/STOP ボタンの enabled を、接続状態と配信状態の組み合わせから決める。</summary>
    private void UpdateActionButtons()
    {
        var connected = _state == ConnectionState.Connected;
        _broadcastButton.SetEnabled(connected && _broadcastState == BroadcastState.Idle);
        _stopButton.SetEnabled(connected && _broadcastState == BroadcastState.Broadcasting);
    }

    private void SetStatus(string text) => _statusLabel.text = text;
}
