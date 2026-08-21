using System;
using System.Collections.Generic;
using System.Linq;
using Livisor.Shared.Common;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 管理者画面のトップレベル View。UIDocument の要素を組み立て、
/// 接続・行編集・Broadcast を <see cref="TimelineHubClient"/>（<see cref="ITimelinePublisher"/>）に橋渡しする。
/// </summary>
/// <remarks>
/// レイアウト・配色をコード側で組み立てているため、Edit Mode でも見た目を確認できるよう
/// <see cref="ExecuteAlways"/> を付けて OnEnable が Play Mode に入る前から実行されるようにしている。
/// </remarks>
[ExecuteAlways]
[RequireComponent(typeof(UIDocument))]
public class AdminConsoleView : MonoBehaviour
{
    [SerializeField] private ServerConfig _serverConfig;
    [SerializeField] private string _roomId = "room1";

    private static readonly List<string> ActionTypeChoices = Enum.GetNames(typeof(ActionType)).ToList();

    private readonly List<TimelineRowModel> _rows = new();

    private ITimelinePublisher _client;

    private TextField _serverAddressField;
    private TextField _roomIdField;
    private Button _connectButton;
    private Label _connectionStatusLabel;
    private ListView _timelineList;
    private Button _broadcastButton;
    private Label _statusLabel;

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

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

        SetupTimelineList();

        _connectButton.clicked += OnConnectClicked;
        _broadcastButton.clicked += OnBroadcastClicked;

        _broadcastButton.SetEnabled(false);
        SetConnectionStatus("未接続");
        SetStatus(string.Empty);

        ApplyLayoutStyles(root);
    }

    private static readonly Color BackgroundColor = new(0.09f, 0.09f, 0.09f, 0.92f);
    private static readonly Color InputBackgroundColor = new(0.2f, 0.2f, 0.2f);
    private static readonly Color InputBorderColor = new(0.35f, 0.35f, 0.35f);
    private static readonly Color TextColor = new(0.9f, 0.9f, 0.9f);
    private static readonly Color MutedTextColor = new(0.7f, 0.7f, 0.7f);
    private static readonly Color RowAltBackgroundColor = new(1f, 1f, 1f, 0.02f);
    private static readonly Color RowAltBackgroundColor2 = new(1f, 1f, 1f, 0.05f);

    /// <summary>
    /// 画面のレイアウト・配色は全てここでインラインスタイルとして組み立てる。
    /// USS（AdminConsole.uss）はこの実機環境ではクラスセレクタが確実に反映されない
    /// ことを確認したため廃止し、見た目に関わる指定は全てコードに一本化している。
    /// </summary>
    private static void ApplyLayoutStyles(VisualElement root)
    {
        root.style.backgroundColor = BackgroundColor;
        root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 16;
        root.style.flexDirection = FlexDirection.Column;
        root.style.flexGrow = 1;

        var connectionPanel = root.Q<VisualElement>("connection-panel");
        SetRow(connectionPanel);
        connectionPanel.style.marginBottom = 8;

        var footer = root.Q<VisualElement>("footer");
        SetRow(footer);
        footer.style.marginTop = 8;
        SetBorderTop(footer);
        footer.style.paddingTop = 8;

        var timelineHeader = root.Q<VisualElement>("timeline-header");
        SetRow(timelineHeader);
        SetRowPadding(timelineHeader);
        timelineHeader.style.marginBottom = 4;
        SetBorderBottom(timelineHeader);

        var timelinePanel = root.Q<VisualElement>("timeline-panel");
        timelinePanel.style.flexDirection = FlexDirection.Column;
        timelinePanel.style.flexGrow = 1;
        timelinePanel.style.alignItems = Align.Stretch;
        timelinePanel.style.marginBottom = 8;

        SetFixedInput(root.Q<TextField>("server-address"), 220);
        SetFixedInput(root.Q<TextField>("room-id"), 110);
        SetButton(root.Q<Button>("connect-button"), 96);
        SetButton(root.Q<Button>("broadcast-button"), 120);

        foreach (var label in root.Query<Label>(className: "field-label").Build().ToList())
        {
            label.style.marginRight = 4;
            label.style.minWidth = 60;
        }

        foreach (var label in root.Query<Label>().Build().ToList())
            label.style.color = TextColor;

        var connectionStatus = root.Q<Label>("connection-status");
        connectionStatus.style.flexGrow = 1;

        var statusLabel = root.Q<Label>("status-label");
        statusLabel.style.flexGrow = 1;

        var headerTime = root.Q<Label>(className: "header-time");
        if (headerTime != null)
        {
            headerTime.style.width = 190;
            headerTime.style.color = MutedTextColor;
        }

        var headerAction = root.Q<Label>(className: "header-action");
        if (headerAction != null)
        {
            headerAction.style.width = 140;
            headerAction.style.color = MutedTextColor;
        }

        var headerValue = root.Q<Label>(className: "header-value");
        if (headerValue != null)
        {
            headerValue.style.flexGrow = 1;
            headerValue.style.color = MutedTextColor;
        }

        var timelineList = root.Q<ListView>("timeline-list");
        timelineList.style.flexGrow = 1;
    }

    private static void SetRow(VisualElement element)
    {
        if (element == null)
            return;

        element.style.flexDirection = FlexDirection.Row;
        element.style.alignItems = Align.Center;
    }

    private static void SetRowPadding(VisualElement element)
    {
        if (element == null)
            return;

        element.style.paddingLeft = element.style.paddingRight
            = element.style.paddingTop = element.style.paddingBottom = 4;
    }

    private static void SetBorderTop(VisualElement element)
    {
        if (element == null)
            return;

        element.style.borderTopWidth = 1;
        element.style.borderTopColor = InputBorderColor;
    }

    private static void SetBorderBottom(VisualElement element)
    {
        if (element == null)
            return;

        element.style.borderBottomWidth = 1;
        element.style.borderBottomColor = InputBorderColor;
    }

    private static void SetFixed(VisualElement element, float width)
    {
        if (element == null)
            return;

        element.style.width = width;
        element.style.flexGrow = 0;
        element.style.flexShrink = 0;
        element.style.marginRight = 8;
    }

    /// <summary>
    /// TextField / IntegerField / DropdownField 等の見た目上の入力欄は、コントロール本体ではなく
    /// 内部の "unity-base-field__input" 系の子要素が背景・文字色を持つ。コントロール本体に
    /// 色を設定しても入力欄には反映されないため、子要素を明示的に探して設定する。
    /// さらにコントロール種別によって実際にテキストを描画する内部要素の構造が異なる
    /// （DropdownField は Label、IntegerField は別のテキスト描画要素など）ため、
    /// 特定のクラス名だけに頼らず配下の TextElement 全てに色を設定する。
    /// </summary>
    private static void SetFixedInput(VisualElement field, float width)
    {
        if (field == null)
            return;

        SetFixed(field, width);
        field.style.color = TextColor;
        field.Query<TextElement>().ForEach(te => te.style.color = TextColor);

        var input = field.Q(className: "unity-base-field__input")
            ?? field.Q(className: "unity-base-text-field__input")
            ?? field.Q(className: "unity-base-popup-field__input");
        if (input == null)
            return;

        input.style.backgroundColor = InputBackgroundColor;
        input.style.color = TextColor;
        input.style.borderTopColor = InputBorderColor;
        input.style.borderBottomColor = InputBorderColor;
        input.style.borderLeftColor = InputBorderColor;
        input.style.borderRightColor = InputBorderColor;
        input.Query<TextElement>().ForEach(te => te.style.color = TextColor);
    }

    private static void SetButton(Button button, float width)
    {
        if (button == null)
            return;

        SetFixed(button, width);
        button.style.backgroundColor = InputBackgroundColor;
        button.style.color = TextColor;
        button.style.borderTopColor = InputBorderColor;
        button.style.borderBottomColor = InputBorderColor;
        button.style.borderLeftColor = InputBorderColor;
        button.style.borderRightColor = InputBorderColor;
    }

    private void OnDisable()
    {
        _connectButton.clicked -= OnConnectClicked;
        _broadcastButton.clicked -= OnBroadcastClicked;
    }

    private async void OnDestroy()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    private void SetupTimelineList()
    {
        _timelineList.itemsSource = _rows;
        _timelineList.reorderable = true;
        _timelineList.showAddRemoveFooter = true;
        _timelineList.showBoundCollectionSize = false;
        // IntegerField の内部入力要素は Label より縦方向に余白を必要とし、32px だと
        // 上下が欠けて見える。余裕を持たせて 44px にする。
        _timelineList.fixedItemHeight = 44;
        _timelineList.makeItem = MakeRow;
        _timelineList.bindItem = BindRow;

        // 「＋」フッタは itemsSource.Add(default) で行を増やすため、参照型の
        // TimelineRowModel には null が入る。bindItem が落ちないよう既定値で埋める。
        _timelineList.itemsAdded += indices =>
        {
            foreach (var i in indices)
                if (i >= 0 && i < _rows.Count && _rows[i] == null)
                    _rows[i] = new TimelineRowModel();
        };
    }

    private VisualElement MakeRow()
    {
        var row = new VisualElement { name = "timeline-row" };
        row.AddToClassList("timeline-row");
        SetRow(row);
        SetRowPadding(row);

        var hoursField = MakeTimePartField("hours-field");
        var minutesField = MakeTimePartField("minutes-field");
        var secondsField = MakeTimePartField("seconds-field");
        var centisecondsField = MakeTimePartField("centiseconds-field");

        var actionDropdown = new DropdownField { name = "action-dropdown", choices = ActionTypeChoices };
        actionDropdown.AddToClassList("action-dropdown");
        SetFixedInput(actionDropdown, 140);

        var numberField = new IntegerField { name = "number-field" };
        numberField.AddToClassList("value-field");
        SetFixedInput(numberField, 80);

        var boolField = new Toggle { name = "bool-field" };
        boolField.AddToClassList("value-field");
        SetFixed(boolField, 80);

        var textField = new TextField { name = "text-field" };
        textField.AddToClassList("value-field");
        SetFixedInput(textField, 80);

        row.Add(hoursField);
        row.Add(MakeTimeSeparator());
        row.Add(minutesField);
        row.Add(MakeTimeSeparator());
        row.Add(secondsField);
        row.Add(MakeTimeSeparator());
        row.Add(centisecondsField);
        row.Add(actionDropdown);
        row.Add(numberField);
        row.Add(boolField);
        row.Add(textField);

        RegisterTimePartCallback(hoursField, row, 0, 23, (model, value) => model.Hours = value);
        RegisterTimePartCallback(minutesField, row, 0, 59, (model, value) => model.Minutes = value);
        RegisterTimePartCallback(secondsField, row, 0, 59, (model, value) => model.Seconds = value);
        RegisterTimePartCallback(centisecondsField, row, 0, 99, (model, value) => model.Centiseconds = value);

        actionDropdown.RegisterValueChangedCallback(evt =>
        {
            if (TryGetRow(row, out var model) && Enum.TryParse<ActionType>(evt.newValue, out var action))
            {
                model.Action = action;
                UpdateValueFieldVisibility(row, action);
            }
        });

        numberField.RegisterValueChangedCallback(evt =>
        {
            if (TryGetRow(row, out var model))
                model.NumberText = evt.newValue.ToString();
        });

        boolField.RegisterValueChangedCallback(evt =>
        {
            if (TryGetRow(row, out var model))
                model.BoolValue = evt.newValue;
        });

        textField.RegisterValueChangedCallback(evt =>
        {
            if (TryGetRow(row, out var model))
                model.TextValue = evt.newValue;
        });

        return row;
    }

    private static IntegerField MakeTimePartField(string name)
    {
        var field = new IntegerField { name = name };
        field.AddToClassList("time-part-field");
        SetFixedInput(field, 46);
        return field;
    }

    private static Label MakeTimeSeparator()
    {
        var label = new Label(":");
        label.AddToClassList("time-separator");
        label.style.color = TextColor;
        label.style.marginLeft = 2;
        label.style.marginRight = 2;
        return label;
    }

    private void RegisterTimePartCallback(IntegerField field, VisualElement row, int min, int max, Action<TimelineRowModel, int> assign)
    {
        field.RegisterValueChangedCallback(evt =>
        {
            var clamped = Mathf.Clamp(evt.newValue, min, max);
            if (clamped != evt.newValue)
                field.SetValueWithoutNotify(clamped);

            if (TryGetRow(row, out var model))
                assign(model, clamped);
        });
    }

    private void BindRow(VisualElement element, int index)
    {
        element.userData = index;
        element.style.backgroundColor = index % 2 == 0 ? RowAltBackgroundColor : RowAltBackgroundColor2;

        if (index < 0 || index >= _rows.Count)
            return;

        // 「＋」フッタの直後は itemsAdded より先に bindItem が呼ばれる場合に備えた保険。
        if (_rows[index] == null)
            _rows[index] = new TimelineRowModel();

        var model = _rows[index];

        element.Q<IntegerField>("hours-field").SetValueWithoutNotify(model.Hours);
        element.Q<IntegerField>("minutes-field").SetValueWithoutNotify(model.Minutes);
        element.Q<IntegerField>("seconds-field").SetValueWithoutNotify(model.Seconds);
        element.Q<IntegerField>("centiseconds-field").SetValueWithoutNotify(model.Centiseconds);

        element.Q<DropdownField>("action-dropdown").SetValueWithoutNotify(model.Action.ToString());

        if (!int.TryParse(model.NumberText, out var number))
            number = 0;
        element.Q<IntegerField>("number-field").SetValueWithoutNotify(number);
        element.Q<Toggle>("bool-field").SetValueWithoutNotify(model.BoolValue);
        element.Q<TextField>("text-field").SetValueWithoutNotify(model.TextValue);

        UpdateValueFieldVisibility(element, model.Action);
    }

    private bool TryGetRow(VisualElement row, out TimelineRowModel model)
    {
        if (row.userData is int index && index >= 0 && index < _rows.Count)
        {
            model = _rows[index];
            return true;
        }

        model = null;
        return false;
    }

    private static void UpdateValueFieldVisibility(VisualElement row, ActionType action)
    {
        var kind = ActionValueKindMap.KindOf(action);
        row.Q<IntegerField>("number-field").style.display = kind == ActionValueKind.Number ? DisplayStyle.Flex : DisplayStyle.None;
        row.Q<Toggle>("bool-field").style.display = kind == ActionValueKind.Bool ? DisplayStyle.Flex : DisplayStyle.None;
        row.Q<TextField>("text-field").style.display = kind == ActionValueKind.Text ? DisplayStyle.Flex : DisplayStyle.None;
    }

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
