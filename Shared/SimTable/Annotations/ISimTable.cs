#nullable enable

namespace SimTable
{
    public interface ISimTable
    {
        int TableId { get; }
        SimHandle Allocate();
        void Free(int stableId);
        void Free(SimHandle handle);
        int Count { get; }
    }
}
