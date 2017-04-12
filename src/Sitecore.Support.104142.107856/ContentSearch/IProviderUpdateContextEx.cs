using System.Collections.Concurrent;
using Sitecore.ContentSearch;

namespace Sitecore.Support.ContentSearch
{
    public interface IProviderUpdateContextEx
    {
        ConcurrentDictionary<IIndexableUniqueId, object> Processed { get; set; }
    }
}