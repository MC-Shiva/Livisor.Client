using System;

namespace Livisor.Shared.Common
{
    /// <summary>
    /// <see cref="ActionValue"/> が保持する値の種類。
    /// </summary>
    public enum ActionValueKind
    {
        Number,
        Bool,
        Text,
    }

    /// <summary>
    /// タイムラインアクションに付随する値。数値 / 真偽値 / 文字列のいずれか 1 つを保持する。
    /// 例: 1 / true / "intro"
    /// 単体では MessagePack 化できない。ワイヤ形式（DTO.TimelineAction.Value 経由での
    /// 生プリミティブ直列化）は Livisor.Shared.DTO 側の関心であり、この型は関与しない。
    /// </summary>
    public readonly struct ActionValue : IEquatable<ActionValue>
    {
        public ActionValueKind Kind { get; }
        public int Number { get; }
        public bool Bool { get; }
        public string Text { get; }

        private ActionValue(ActionValueKind kind, int number, bool boolValue, string text)
        {
            Kind = kind;
            Number = number;
            Bool = boolValue;
            Text = text;
        }

        public static ActionValue From(int value) => new(ActionValueKind.Number, value, default, string.Empty);

        public static ActionValue From(bool value) => new(ActionValueKind.Bool, default, value, string.Empty);

        public static ActionValue From(string? value) => new(ActionValueKind.Text, default, default, value ?? string.Empty);

        public static implicit operator ActionValue(int value) => From(value);

        public static implicit operator ActionValue(bool value) => From(value);

        public static implicit operator ActionValue(string value) => From(value);

        public override string ToString() => Kind switch
        {
            ActionValueKind.Number => Number.ToString(),
            ActionValueKind.Bool => Bool.ToString(),
            ActionValueKind.Text => Text,
            _ => string.Empty,
        };

        public bool Equals(ActionValue other) => Kind == other.Kind
            && Number == other.Number
            && Bool == other.Bool
            && Text == other.Text;

        public override bool Equals(object obj) => obj is ActionValue other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Number, Bool, Text);
    }
}
