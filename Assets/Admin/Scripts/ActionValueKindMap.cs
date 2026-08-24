using Livisor.Shared.Common;

/// <summary>
/// ActionType ごとに入力すべき ActionValue の種類（Number / Bool / Text）を固定するマップ。
/// ActionType が増えたときはここに 1 行追加する。
/// </summary>
public static class ActionValueKindMap
{
    public static ActionValueKind KindOf(ActionType action) => action switch
    {
        ActionType.Start => ActionValueKind.Number,
        ActionType.Stop => ActionValueKind.Number,
        ActionType.VolumeChange => ActionValueKind.Number,
        _ => ActionValueKind.Number,
    };
}
