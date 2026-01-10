using ExpenseTracker.Application.Mapping;

namespace ExpenseTracker.Application.Mapping
{
    public static class MappingHelper
    {
        public static List<TOut> ToList<TIn, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, TOut> map)
        {
            if (source == null)
                return new List<TOut>();

            return source.Select(map).ToList();
        }
    }
}