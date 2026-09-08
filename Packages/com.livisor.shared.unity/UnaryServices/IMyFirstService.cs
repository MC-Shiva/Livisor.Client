using MagicOnion;

namespace Livisor.Shared.UnaryServices
{
    public interface IMyFirstService : IService<IMyFirstService>
    {
        UnaryResult<int> SumAsync(int x, int y);
    }
}
