using System;
using System.Collections.Generic;
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

    private TextField _serverAddressField;
    private TextField _roomIdField;
    private Button _connectButton;
    private Label _connectionStatusLabel;
    private ListView _timelineList;
    private Button _broadcastButton;
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
        _timelineList = root.Q<ListView>("timeline-list");
        _broadcastButton = root.Q<Button>("broadcast-button");
        _statusLabel = root.Q<Label>("status-label");

        _serverAddressField.value = _serverConfig != null ? _serverConfig.ServerAddress : string.Empty;
        _roomIdField.value = _roomId;

        if (_rows.Count == 0)
            _rows.Add(new TimelineRowModel());

        _timelineController = new TimelineListController(_timelineList, _rows, _timelineRowTemplate, _timelineFooterTemplate);
        _timelineController.Setup();

        _connectButton.clicked += OnConnectClicked;
        _broadcastButton.clicked += OnBroadcastClicked;

        _broadcastButton.SetEnabled(false);
        SetConnectionStatus("Not Connected");
        SetStatus(string.Empty);
    }

    /// <summary>OnEnable で購読したイベントを解除する。再有効化時に OnEnable が再購読するため、多重購読を防ぐ。</summary>
    private void OnDisable()
    {
        _connectButton.clicked -= OnConnectClicked;
        _broadcastButton.clicked -= OnBroadcastClicked;
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
    /// 接続ボタン押下時の処理。再接続時に古い接続が残らないよう、既存クライアントがあれば
    /// 先に DisposeAsync してから新規接続する。
    /// </summary>
    private async void OnConnectClicked()
    {
        _connectButton.SetEnabled(false);
        _broadcastButton.SetEnabled(false);
        SetConnectionStatus("接続中...");

        try
        {
            if (_client != null)
                await _client.DisposeAsync();

            var hubClient = new TimelineHubClient();
            _client = hubClient;
            await hubClient.ConnectAsync(_serverAddressField.value, _roomIdField.value);

            SetConnectionStatus($"接続済み（room: {_roomIdField.value}）");
            _broadcastButton.SetEnabled(true);
        }
        catch (Exception e)
        {
            SetConnectionStatus("接続失敗");
            SetStatus(e.Message);
            Debug.LogException(e);
        }
        finally
        {
            _connectButton.SetEnabled(true);
        }
    }

    /// <summary>
    /// Broadcast ボタン押下時の処理。<see cref="TimelineDraft.TryBuild"/> で行データを検証・変換してから送信する。
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
            SetStatus($"送信しました（{actions.Length} 件）。");
        }
        catch (Exception e)
        {
            SetStatus($"送信に失敗しました: {e.Message}");
            Debug.LogException(e);
        }
        finally
        {
            _broadcastButton.SetEnabled(true);
        }
    }

    private void SetConnectionStatus(string text) => _connectionStatusLabel.text = text;

    private void SetStatus(string text) => _statusLabel.text = text;
}
