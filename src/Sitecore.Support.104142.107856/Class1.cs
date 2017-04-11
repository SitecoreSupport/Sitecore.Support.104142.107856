using Sitecore.Configuration;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Pipelines.GetDependencies;
using Sitecore.Data;
using Sitecore.Data.Databases;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.SecurityModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Sitecore.Support.ContentSearch
{
    public class SitecoreItemCrawler : Sitecore.ContentSearch.SitecoreItemCrawler
    {
        public SitecoreItemCrawler()
        {
        }

        private bool IsRootOrDescendant(ID id)
        {
            if (this.RootItem.ID == id)
                return true;
            Database database = Factory.GetDatabase(this.Database);
            Item obj;
            using (new SecurityDisabler())
                obj = database.GetItem(id);
            if (obj != null)
                return this.IsAncestorOf(obj);
            return false;
        }

        public override void Update(IProviderUpdateContext context, IIndexableUniqueId indexableUniqueId, IndexEntryOperationContext operationContext, IndexingOptions indexingOptions = 0)
        {
            Assert.ArgumentNotNull((object)indexableUniqueId, "indexableUniqueId");
            IProviderUpdateContextEx iproviderUpdateContextEx = context as IProviderUpdateContextEx;
            if (base.IsExcludedFromIndex(indexableUniqueId, operationContext, true) || iproviderUpdateContextEx != null && !iproviderUpdateContextEx.Processed.TryAdd(indexableUniqueId, (object)null) || !ShouldStartIndexing(indexingOptions))
                return;
            Assert.IsNotNull((object)((Crawler<SitecoreIndexableItem>)this).DocumentOptions, "DocumentOptions");
            if (base.IsExcludedFromIndex(indexableUniqueId, operationContext, true))
                return;
            if (operationContext != null)
            {
                if (operationContext.NeedUpdateChildren)
                {
                    Item obj = Sitecore.Data.Database.GetItem(new SitecoreItemUniqueId(indexableUniqueId as SitecoreItemUniqueId));
                    if (obj != null)
                    {
                        if (operationContext.OldParentId != Guid.Empty && this.IsRootOrDescendant(new ID(operationContext.OldParentId)) && !this.IsAncestorOf(obj))
                        {
                            ((Crawler<SitecoreIndexableItem>)this).Delete(context, indexableUniqueId, (IndexingOptions)0);
                            return;
                        }
                      this.UpdateHierarchicalRecursive(context, new SitecoreIndexableItem(obj), CancellationToken.None);
                        return;
                    }
                }
                if (operationContext.NeedUpdatePreviousVersion)
                {
                    Item obj = Sitecore.Data.Database.GetItem(new SitecoreItemUniqueId(indexableUniqueId as SitecoreItemUniqueId));
                    if (obj != null)
                        this.UpdatePreviousVersion(obj, context);
                }
                if (operationContext.NeedUpdateAllVersions)
                {
                    Item obj = Sitecore.Data.Database.GetItem(new SitecoreItemUniqueId(indexableUniqueId as SitecoreItemUniqueId));
                    if (obj == null)
                        return;
                    DoUpdate(context, new SitecoreIndexableItem(obj), operationContext);
                    return;
                }
            }
            SitecoreIndexableItem indexableAndCheckDeletes = GetIndexableAndCheckDeletes(indexableUniqueId);
            if (indexableAndCheckDeletes != null)
                DoUpdate(context, indexableAndCheckDeletes, operationContext);
            else if (GroupShouldBeDeleted(indexableUniqueId.GroupId))
                Delete(context, indexableUniqueId.GroupId, (IndexingOptions)0);
            else
                Delete(context, indexableUniqueId, (IndexingOptions)0);
        }

        protected override bool IsExcludedFromIndex(IIndexableUniqueId indexableUniqueId, IndexEntryOperationContext operationContext, bool checkLocation)
        {
            ItemUri itemUri = new SitecoreItemUniqueId(indexableUniqueId as SitecoreItemUniqueId);
            if (itemUri != (ItemUri)null && !itemUri.DatabaseName.Equals(this.Database, StringComparison.InvariantCultureIgnoreCase))
                return true;
            if (!checkLocation || operationContext != null && operationContext.OldParentId != Guid.Empty && this.IsRootOrDescendant(new ID(operationContext.OldParentId)))
                return false;
            Item obj = Sitecore.Data.Database.GetItem(new SitecoreItemUniqueId(indexableUniqueId as SitecoreItemUniqueId));
            if (obj != null)
                return !this.IsAncestorOf(obj);
            return false;
        }

        private void UpdatePreviousVersion(Item item, IProviderUpdateContext context)
        {
            Sitecore.Data.Version[] versionArray;
            using (new WriteCachesDisabler())
                versionArray = item.Versions.GetVersionNumbers() ?? new Sitecore.Data.Version[0];
            int index = ((IEnumerable<Sitecore.Data.Version>)versionArray).ToList<Sitecore.Data.Version>().FindIndex((Predicate<Sitecore.Data.Version>)(version => version.Number == item.Version.Number));
            if (index < 1)
                return;
            Sitecore.Data.Version previousVersion = versionArray[index - 1];
            Sitecore.Data.Version version1 = ((IEnumerable<Sitecore.Data.Version>)versionArray).FirstOrDefault<Sitecore.Data.Version>((Func<Sitecore.Data.Version, bool>)(v => v == previousVersion));
            SitecoreIndexableItem sitecoreIndexableItem = new SitecoreIndexableItem(Sitecore.Data.Database.GetItem(new ItemUri(item.ID, item.Language, version1, item.Database.Name)));
            if (sitecoreIndexableItem == null)
                return;
            ((IIndexableBuiltinFields)sitecoreIndexableItem).IsLatestVersion = false;
            sitecoreIndexableItem.IndexFieldStorageValueFormatter = context.Index.Configuration.IndexFieldStorageValueFormatter;
            Operations.Update((IIndexable)sitecoreIndexableItem, context, base.index.Configuration);
        }

        public override void Update(IProviderUpdateContext context, IIndexableUniqueId indexableUniqueId, IndexingOptions indexingOptions = 0)
        {
            IProviderUpdateContextEx providerUpdateContextEx = context as IProviderUpdateContextEx;
            if (IsExcludedFromIndex(GetIndexable(indexableUniqueId), true) || providerUpdateContextEx != null && !providerUpdateContextEx.Processed.TryAdd(indexableUniqueId, (object)null))
                return;
            base.Update(context, indexableUniqueId, indexingOptions);
        }

        protected override void UpdateDependents(IProviderUpdateContext context, SitecoreIndexableItem indexable)
        {
            IProviderUpdateContextEx providerUpdateContextEx = context as IProviderUpdateContextEx;
            using (IEnumerator<IIndexableUniqueId> enumerator1 = GetDependenciesPipeline.GetIndexingDependencies((IIndexable)indexable).Where<IIndexableUniqueId>((Func<IIndexableUniqueId, bool>)(i => !IsExcludedFromIndex(indexable, true))).GetEnumerator())
            {
                while (((IEnumerator)enumerator1).MoveNext())
                {
                    IIndexableUniqueId current = enumerator1.Current;
                    object obj;
                    if (providerUpdateContextEx == null || !providerUpdateContextEx.Processed.TryGetValue(current, out obj))
                    {
                        using (IEnumerator<IProviderCrawler> enumerator2 = ((IEnumerable<IProviderCrawler>)((Crawler<SitecoreIndexableItem>)this).Index.Crawlers).GetEnumerator())
                        {
                            while (((IEnumerator)enumerator2).MoveNext())
                                enumerator2.Current.Update(context, current, (IndexingOptions)0);
                        }
                    }
                }
            }
        }
    }

}

namespace Sitecore.Support.ContentSearch
{
    using System.Collections.Concurrent;

    public interface IProviderUpdateContextEx
    {
        ConcurrentDictionary<IIndexableUniqueId, object> Processed { get; set; }
    }
}


namespace Sitecore.Support
{
    using Shell.Framework.Pipelines;

    public class Custom : CloneItems
    {
        public void Do(CopyItemsArgs args)
        {
            Log.Info("Test", (object)this);
        }
    }
}