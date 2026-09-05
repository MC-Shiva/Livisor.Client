using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 管理者画面のトップレベル View。UIDocument の要素を組み立て、
/// 接続・再生・停止・予約・音量の操作を <see cref="IRoomClient"/> に橋渡しする。
/// タイムライン行の UI 構築・バインドは <see cref="TimelineListController"/> の責務とし、ここでは持たない。
/// 見た目（レイアウト・配色）は UXML / USS 側の責務とし、ここでは持たない。
///
/// 通信の内訳（サーバー側の契約に合わせている）:
///   - 再生 / 停止 / 予約 / 予約取消 … 一度きりの操作。Unary の ITimelineService
///   - 音量 … 変わり続ける値。StreamingHub の IRoomStateHub に publish する
/// 予約は 1 件だけ持てる。タイムライン一覧の先頭行を「再生開始からの相対時間」として送る。
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

    private IRoomClient _client;
    private TimelineListController _timelineController;
    private ConnectionState _state = ConnectionState.Disconnected;

    // サーバーからの押し出しは非メインスレッドで届くため、UI に触るのは Update（メインスレッド）で行う。
    private readonly ConcurrentQueue<TransportState> _pendingTransports = new();
    private readonly ConcurrentQueue<RoomStatePatch> _pendingStates = new();

    private TextField _serverAddressField;
    private TextField _roomIdField;
    private Button _connectButton;
    private Label _connectionStatusLabel;
    private VisualElement _statusDot;
    private VisualElement _statusDotGlow;
    private Button _playButton;
    private Button _stopButton;
    private Label _transportLabel;
    private IntegerField _volumeField;
    private Button _volumeButton;
    private Label _volumeLabel;
    private ListView _timelineList;
    private Button _scheduleButton;
    private Button _cancelScheduleButton;
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
        _playButton = root.Q<Button>("play-button");
        _stopButton = root.Q<Button>("stop-button");
        _transportLabel = root.Q<Label>("transport-status");
        _volumeField = root.Q<IntegerField>("volume-field");
        _volumeButton = root.Q<Button>("volume-button");
        _volumeLabel = root.Q<Label>("volume-status");
        _timelineList = root.Q<ListView>("timeline-list");
        _scheduleButton = root.Q<Button>("schedule-button");
        _cancelScheduleButton = root.Q<Button>("cancel-schedule-button");
        _statusLabel = root.Q<Label>("status-label");

        _serverAddressField.value = _serverConfig != null ? _serverConfig.ServerAddress : string.Empty;
        _roomIdField.value = _roomId;

        if (_rows.Count == 0)
            _rows.Add(new TimelineRowModel());

        _timelineController = new TimelineListController(_timelineList, _rows, _timelineRowTemplate, _timelineFooterTemplate);
        _timelineController.Setup();

        _connectButton.clicked += OnConnectClicked;
        _playButton.clicked += OnPlayClicked;
        _stopButton.clicked += OnStopClicked;
        _volumeButton.clicked += OnVolumeClicked;
        _scheduleButton.clicked += OnScheduleClicked;
        _cancelScheduleButton.clicked += OnCancelScheduleClicked;

        SetState(ConnectionState.Disconnected);
        SetStatus(string.Empty);
        _transportLabel.text = "-";
        _volumeLabel.text = "-";
    }

    /// <summary>OnEnable で購読したイベントを解除する。再有効化時に OnEnable が再購読するため、多重購読を防ぐ。</summary>
    private void OnDisable()
    {
        if (_connectButton != null) _connectButton.clicked -= OnConnectClicked;
        if (_playButton != null) _playButton.clicked -= OnPlayClicked;
        if (_stopButton != null) _stopButton.clicked -= OnStopClicked;
        if (_volumeButton != null) _volumeButton.clicked -= OnVolumeClicked;
        if (_scheduleButton != null) _scheduleButton.clicked -= OnScheduleClicked;
        if (_cancelScheduleButton != null) _cancelScheduleButton.clicked -= OnCancelScheduleClicked;
    }

    /// <summary>破棄時に接続中のクライアントを DisposeAsync で確実に切断する。</summary>
    private async void OnDestroy()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    private void Update()
    {
        while (_pendingStates.TryDequeue(out var patch))
            ShowState(patch);

        while (_pendingTransports.TryDequeue(out var state))
            ShowTransport(state);
    }

    // === 接続 ===

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
    /// DisposeAsync で破棄し、操作ボタンの null ガードが正しく効く状態に戻す。
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

            var client = new RoomClient();
            client.TransportChanged += state => _pendingTransports.Enqueue(state);
            client.StateChanged += patch => _pendingStates.Enqueue(patch);
            _client = client;
            await client.ConnectAsync(serverAddress, roomId);

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
        _transportLabel.text = "-";
        _volumeLabel.text = "-";
    }

    // === 操作 ===

    private void OnPlayClicked() => _ = RunAsync("再生", () => _client.PlayAsync());

    private void OnStopClicked() => _ = RunAsync("停止", () => _client.StopAsync());

    private void OnCancelScheduleClicked() => _ = RunAsync("予約取消", () => _client.CancelScheduledActionAsync());

    /// <summary>
    /// タイムライン一覧の先頭行を予約として送る。<see cref="TimelineDraft.TryBuild"/> で検証・変換したうえで、
    /// 時刻の昇順で最初の 1 件だけを使う（サーバーの予約は 1 件だけ）。
    /// 行の時刻は「再生開始からの相対時間」として扱われる。
    /// </summary>
    private void OnScheduleClicked()
    {
        if (!TimelineDraft.TryBuild(_rows, out var actions, out var error))
        {
            SetStatus(error);
            return;
        }

        if (actions.Length > 1)
            SetStatus($"予約は 1 件だけです。先頭の {actions[0].Time} を送ります（他 {actions.Length - 1} 件は送りません）。");

        _ = RunAsync($"予約 {actions[0].Time}", () => _client.ScheduleActionAsync(actions[0]));
    }

    /// <summary>音量を状態同期に publish する。同じ room の全員に届き、自分にも戻ってくる。</summary>
    private async void OnVolumeClicked()
    {
        if (_client == null)
        {
            SetStatus("先に接続してください。");
            return;
        }

        var percent = _volumeField.value;
        if (percent < 0 || percent > 100)
        {
            SetStatus("音量は 0〜100 の整数で入力してください。");
            return;
        }

        _volumeButton.SetEnabled(false);
        try
        {
            await _client.PublishStateAsync(new RoomStateEntry { Key = RoomStateKeys.Volume, Value = ActionValue.From(percent) });
            SetStatus($"音量 {percent} を送りました。");
        }
        catch (Exception e)
        {
            SetStatus($"音量の送信に失敗しました: {e.Message}");
            Debug.LogException(e);
        }
        finally
        {
            _volumeButton.SetEnabled(true);
        }
    }

    // 再生・停止・予約は同じ形（Unary を 1 回呼び、確定したトランスポートが返る）なのでまとめる。
    private async Task RunAsync(string label, Func<Task<TransportState>> operation)
    {
        if (_client == null)
        {
            SetStatus("先に接続してください。");
            return;
        }

        SetOperationButtonsEnabled(false);
        SetStatus($"{label}を送信中...");

        try
        {
            var state = await operation();
            ShowTransport(state);
            SetStatus($"{label}を送りました。");
        }
        catch (Exception e)
        {
            SetStatus($"{label}に失敗しました: {e.Message}");
            Debug.LogException(e);
        }
        finally
        {
            SetOperationButtonsEnabled(_state == ConnectionState.Connected);
        }
    }

    // === 表示 ===

    private void ShowTransport(TransportState state)
    {
        var scheduled = state.ScheduledAction == null
            ? "予約なし"
            : $"予約 {state.ScheduledAction.Time} {state.ScheduledAction.Action}={state.ScheduledAction.Value}";
        _transportLabel.text = $"{(state.Playing ? "再生中" : "停止中")} / {scheduled}";
    }

    private void ShowState(RoomStatePatch patch)
    {
        foreach (var entry in patch.Entries)
        {
            if (entry.Key == RoomStateKeys.Volume)
                _volumeLabel.text = $"音量 {entry.Value}";
        }
    }

    /// <summary>
    /// 接続状態を切り替える。CONNECT/DISCONNECT ボタンの表示・enabled、操作ボタンの enabled、
    /// connection-status ラベルと status-dot の見た目をここに集約する。
    /// </summary>
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
        SetOperationButtonsEnabled(state == ConnectionState.Connected);

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
    }

    private void SetOperationButtonsEnabled(bool enabled)
    {
        _playButton.SetEnabled(enabled);
        _stopButton.SetEnabled(enabled);
        _volumeButton.SetEnabled(enabled);
        _scheduleButton.SetEnabled(enabled);
        _cancelScheduleButton.SetEnabled(enabled);
    }

    private void SetStatus(string text) => _statusLabel.text = text;
}
