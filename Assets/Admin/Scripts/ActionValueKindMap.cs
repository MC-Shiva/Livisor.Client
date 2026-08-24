using Livisor.Shared.Common;

/// <summary>
/// ActionType ごとに入力すべき ActionValue の種類（Number / Bool / Text）を固定するマップ。
/// </summary>
/// <remarks>
/// Server 側でも検証したくなった場合は Livisor.Shared へ移す。
/// </remarks>
public static class ActionValueKindMap
{
    public static ActionValueKind KindOf(ActionType action) => action switch
    {
        ActionType.Play => ActionValueKind.Bool,
        ActionType.VolumeChange => ActionValueKind.Number,
        _ => ActionValueKind.Number,
    };
}
