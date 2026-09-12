using Livisor.Shared.Common;
using MessagePack;
using MessagePack.Formatters;

namespace Livisor.Shared.DTO
{
    /// <summary>
    /// <see cref="ActionValue"/> を MessagePack 上の生プリミティブ（int / bool / string）として
    /// 読み書きするフォーマッタ。<see cref="TimelineAction.Value"/> に付けた
    /// <see cref="MessagePackFormatterAttribute"/> から解決されるため、Resolver への登録は不要。
    /// </summary>
    public sealed class ActionValueFormatter : IMessagePackFormatter<ActionValue>
    {
        public void Serialize(ref MessagePackWriter writer, ActionValue value, MessagePackSerializerOptions options)
        {
            switch (value.Kind)
            {
                case ActionValueKind.Bool:
                    writer.Write(value.Bool);
                    break;
                case ActionValueKind.Text:
                    writer.Write(value.Text);
                    break;
                case ActionValueKind.Number:
                default:
                    writer.Write(value.Number);
                    break;
            }
        }

        public ActionValue Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            switch (reader.NextMessagePackType)
            {
                case MessagePackType.Boolean:
                    return ActionValue.From(reader.ReadBoolean());
                case MessagePackType.String:
                    return ActionValue.From(reader.ReadString());
                case MessagePackType.Integer:
                    return ActionValue.From(reader.ReadInt32());
                case MessagePackType.Nil:
                    reader.ReadNil();
                    return ActionValue.From(0);
                default:
                    throw new MessagePackSerializationException(
                        $"ActionValue に対応しない MessagePack 型です: {reader.NextMessagePackType}");
            }
        }
    }
}
