using System;
using System.Collections.Generic;
using System.Linq;
using Livisor.Shared.Common;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// タイムライン ListView の行 UI 構築・バインドを担当する。
/// AdminConsoleView から呼び出される非 MonoBehaviour のヘルパークラス。
/// 行・フッターの構造は TimelineRow.uxml / TimelineFooter.uxml 側の責務とし、ここでは持たない。
/// </summary>
public class TimelineListController
{
    private static readonly List<string> ActionTypeChoices = Enum.GetNames(typeof(ActionType)).ToList();

    private static readonly string[] CueAccentClasses =
    {
        "timeline-item--cue-0", "timeline-item--cue-1", "timeline-item--cue-2"
    };

    private readonly ListView _listView;
    private readonly List<TimelineRowModel> _rows;
    private readonly VisualTreeAsset _rowTemplate;
    private readonly VisualTreeAsset _footerTemplate;

    // 配信中に発火済みの行の index。未配信・未発火なら -1。
    private int _activeIndex = -1;

    public TimelineListController(ListView listView, List<TimelineRowModel> rows, VisualTreeAsset rowTemplate, VisualTreeAsset footerTemplate)
    {
        _listView = listView;
        _rows = rows;
        _rowTemplate = rowTemplate;
        _footerTemplate = footerTemplate;
    }

    public void Setup()
    {
        _listView.itemsSource = _rows;
        _listView.reorderable = true;
        _listView.showBoundCollectionSize = false;
        _listView.fixedItemHeight = 44;
        _listView.makeItem = CreateRowElement;
        _listView.bindItem = ApplyRowData;
        _listView.makeFooter = CreateFooterElement;
    }

    /// <summary>
    /// 配信中に発火済みの行の index を切り替え、ハイライト表示に反映する。
    /// 値が変わらないときは RefreshItems を呼ばない（毎フレーム呼ぶと入力欄のフォーカスが飛ぶため）。
    /// </summary>
    public void SetActiveIndex(int index)
    {
        if (_activeIndex == index)
            return;

        _activeIndex = index;
        _listView.RefreshItems();
    }

    /// <summary>
    /// TimelineFooter.uxml を複製し、フッター（－ / ＋ ADD CUE）を組み立てる。
    /// row と異なり可視スロットぶん複数作られることはなく、リスト全体で唯一のインスタンスとして 1 回だけ呼ばれる。
    /// AdminConsole.uxml に直書きしない理由: 行は実行時に動的生成されるため、UXML 上に「行の末尾」という
    /// 位置がそもそも存在しない。ListView はその末尾へフッターを自分で挿入する仕組みとして、
    /// 子要素の静的定義ではなくコールバック（makeFooter）で VisualElement を受け取る API を提供している。
    /// </summary>
    private VisualElement CreateFooterElement()
    {
        var footer = InstantiateTemplate(_footerTemplate, "timeline-footer");

        var removeButton = footer.Q<Button>("timeline-remove-button");
        removeButton.clicked += () =>
        {
            if (_rows.Count == 0)
                return;

            var index = _listView.selectedIndex >= 0 ? _listView.selectedIndex : _rows.Count - 1;
            _rows.RemoveAt(index);
            _listView.RefreshItems();
        };

        var addButton = footer.Q<Button>("timeline-add-button");
        addButton.clicked += () =>
        {
            _rows.Add(new TimelineRowModel());
            _listView.RefreshItems();
        };

        return footer;
    }

    /// <summary>
    /// TimelineRow.uxml を複製し、行 1 つぶんの要素とイベント購読を組み立てる。
    /// ListView の仮想化により、可視スロットの数だけ（画面に映る行数ぶん）呼ばれる。
    /// 値の反映は行わない（<see cref="ApplyRowData"/> の責務）。
    /// </summary>
    private VisualElement CreateRowElement()
    {
        var row = InstantiateTemplate(_rowTemplate, "timeline-row");

        var hoursField = row.Q<IntegerField>("hours-field");
        var minutesField = row.Q<IntegerField>("minutes-field");
        var secondsField = row.Q<IntegerField>("seconds-field");
        var centisecondsField = row.Q<IntegerField>("centiseconds-field");

        var actionDropdown = row.Q<DropdownField>("action-dropdown");
        actionDropdown.choices = ActionTypeChoices;

        var numberField = row.Q<IntegerField>("number-field");
        var boolField = row.Q<Toggle>("bool-field");
        var textField = row.Q<TextField>("text-field");

        var removeButton = row.Q<Button>("remove-button");
        removeButton.clicked += () =>
        {
            if (row.userData is int index && index >= 0 && index < _rows.Count)
            {
                _rows.RemoveAt(index);
                _listView.RefreshItems();
            }
        };

        RegisterTimePartCallback(hoursField, row, 0, 23, (model, value) => model.Hours = value);
        RegisterTimePartCallback(minutesField, row, 0, 59, (model, value) => model.Minutes = value);
        RegisterTimePartCallback(secondsField, row, 0, 59, (model, value) => model.Seconds = value);
        RegisterTimePartCallback(centisecondsField, row, 0, 99, (model, value) => model.Centiseconds = value);

        actionDropdown.RegisterValueChangedCallback(evt =>
        {
            if (TryGetBoundModel(row, out var model) && Enum.TryParse<ActionType>(evt.newValue, out var action))
            {
                model.Action = action;
                UpdateValueFieldVisibility(row, action);
            }
        });

        numberField.RegisterValueChangedCallback(evt =>
        {
            if (TryGetBoundModel(row, out var model))
                model.NumberText = evt.newValue.ToString();
        });

        boolField.RegisterValueChangedCallback(evt =>
        {
            if (TryGetBoundModel(row, out var model))
                model.BoolValue = evt.newValue;
        });

        textField.RegisterValueChangedCallback(evt =>
        {
            if (TryGetBoundModel(row, out var model))
                model.TextValue = evt.newValue;
        });

        return row;
    }

    /// <summary>
    /// スクロールで行の中身が入れ替わるたびに、
    /// 既存の要素へモデルの値を反映する。
    /// </summary>
    private void ApplyRowData(VisualElement element, int index)
    {
        element.userData = index;

        foreach (var cueClass in CueAccentClasses)
            element.RemoveFromClassList(cueClass);
        if (index >= 0)
            element.AddToClassList(CueAccentClasses[index % CueAccentClasses.Length]);

        element.EnableInClassList("timeline-item--active", index == _activeIndex);

        if (index < 0 || index >= _rows.Count)
            return;

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
    
    /// <summary>
    /// UXML テンプレートを複製する。CloneTree は複製した内容を TemplateContainer という
    /// ラッパー要素の子として返すため、rootName で指定した中身の要素だけを検索して切り離し、返す。
    /// </summary>
    private static VisualElement InstantiateTemplate(VisualTreeAsset template, string rootName)
    {
        var container = template.CloneTree();
        var root = container.Q<VisualElement>(rootName);
        root.RemoveFromHierarchy();
        return root;
    }

    /// <summary>時間欄の値変更を [min, max] にクランプしつつ、対応するモデルのプロパティへ書き込む。</summary>
    private void RegisterTimePartCallback(IntegerField field, VisualElement row, int min, int max, Action<TimelineRowModel, int> assign)
    {
        field.RegisterValueChangedCallback(evt =>
        {
            var clamped = Mathf.Clamp(evt.newValue, min, max);
            if (clamped != evt.newValue)
                field.SetValueWithoutNotify(clamped);

            if (TryGetBoundModel(row, out var model))
                assign(model, clamped);
        });
    }
    
    /// <summary>行要素の userData（ApplyRowData で設定した index）から、現在バインドされているモデルを取得する。</summary>
    private bool TryGetBoundModel(VisualElement row, out TimelineRowModel model)
    {
        if (row.userData is int index && index >= 0 && index < _rows.Count)
        {
            model = _rows[index];
            return true;
        }

        model = null;
        return false;
    }

    /// <summary>Action の値種別（数値/真偽値/文字列）に応じて、対応する入力欄だけを表示する。</summary>
    private static void UpdateValueFieldVisibility(VisualElement row, ActionType action)
    {
        var kind = ActionValueKindMap.KindOf(action);
        row.Q<IntegerField>("number-field").style.display = kind == ActionValueKind.Number ? DisplayStyle.Flex : DisplayStyle.None;
        row.Q<Toggle>("bool-field").style.display = kind == ActionValueKind.Bool ? DisplayStyle.Flex : DisplayStyle.None;
        row.Q<TextField>("text-field").style.display = kind == ActionValueKind.Text ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
