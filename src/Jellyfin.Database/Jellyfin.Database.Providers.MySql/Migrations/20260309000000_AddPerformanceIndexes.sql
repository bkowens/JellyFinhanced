-- =============================================================================
-- Migration: 20260309000000_AddPerformanceIndexes
-- Target:    MySQL 8.0 / MariaDB 10.6+
-- Project:   JellyFinhanced (MySQL-backed Jellyfin fork)
-- Author:    Deep indexing analysis — 2026-03-09
-- =============================================================================
--
-- Priority legend:
--   CRITICAL  missing index causes full table scan on the most common read paths
--   HIGH      significant slowdown at 10 000+ items
--   MEDIUM    noticeable at 50 000+ items
--   LOW       marginal gain; optimise last
--
-- All DDL uses IF NOT EXISTS / IF EXISTS guards so the script is idempotent
-- and safe to re-run on a partially-applied schema.
-- =============================================================================


-- =============================================================================
-- TABLE: BaseItems
-- =============================================================================

-- ---------------------------------------------------------------------------
-- [CRITICAL] Library browse
-- Pattern: WHERE Type IN (...) AND IsVirtualItem = 0 ORDER BY SortName
-- Source:  TranslateQuery (IncludeItemTypes / ExcludeItemTypes + IsVirtualItem)
--          ApplyOrder fallback: ORDER BY SortName
-- Why new: No existing index leads with (Type, IsVirtualItem).  The optimizer
--          cannot use any existing index to satisfy both predicates and avoid a
--          filesort simultaneously.  SortName is varchar(128) — no prefix needed.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_Type_IsVirtualItem_SortName
    ON BaseItems (Type, IsVirtualItem, SortName);


-- ---------------------------------------------------------------------------
-- [CRITICAL] Parent-folder browsing
-- Pattern: WHERE ParentId = ? AND IsVirtualItem = 0 AND Type = ?
-- Source:  TranslateQuery line: baseQuery.Where(e => e.ParentId!.Value == filter.ParentId)
--          combined with IsVirtualItem and IncludeItemTypes filters
-- Why new: IX_BaseItems_ParentId is single-column.  Every folder-contents
--          request must post-filter IsVirtualItem and Type over the full set of
--          children, which is a large portion of the table for root libraries.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_ParentId_IsVirtualItem_Type
    ON BaseItems (ParentId, IsVirtualItem, Type);


-- ---------------------------------------------------------------------------
-- [CRITICAL] Recently added / Latest shelf
-- Pattern: WHERE Type = ? AND IsVirtualItem = 0 ORDER BY DateCreated DESC
-- Source:  GetLatestItemList, home-screen Latest row, ItemsController with
--          SortBy=DateCreated
-- Why new: Existing indexes that include DateCreated all lead with TopParentId
--          (IX_BaseItems_IsFolder_TopParentId_IsVirtualItem_PresentationUni~ and
--           IX_BaseItems_Type_TopParentId_IsVirtualItem_PresentationUniqueK~).
--          When TopParentId is not constrained (global latest), those indexes
--          cannot be used for this ORDER BY.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_Type_IsVirtualItem_DateCreated
    ON BaseItems (Type, IsVirtualItem, DateCreated DESC);


-- ---------------------------------------------------------------------------
-- [CRITICAL] Series -> Episode hierarchy
-- Pattern: WHERE SeriesId = ? AND IsVirtualItem = 0
-- Source:  TvShowsController.GetEpisodes, GetNextUpSeriesKeys, season listing
-- Why new: No index on SeriesId exists anywhere in the initial migration.
--          A series with 500 episodes forces a full scan of ~50 000-row tables.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_SeriesId_IsVirtualItem
    ON BaseItems (SeriesId, IsVirtualItem);


-- ---------------------------------------------------------------------------
-- [CRITICAL] Season -> Episode hierarchy
-- Pattern: WHERE SeasonId = ? AND IsVirtualItem = 0
-- Source:  TvShowsController.GetEpisodes with SeasonId filter
-- Why new: No index on SeasonId exists in the initial migration.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_SeasonId_IsVirtualItem
    ON BaseItems (SeasonId, IsVirtualItem);


-- ---------------------------------------------------------------------------
-- [HIGH] SeriesPresentationUniqueKey lookups — Next Up / Continue Watching
-- Pattern: WHERE SeriesPresentationUniqueKey = ?
--          GROUP BY SeriesPresentationUniqueKey
-- Source:  GetNextUpSeriesKeys, TranslateQuery SeriesPresentationUniqueKey filter,
--          ApplyGroupingFilter GroupBySeriesPresentationUniqueKey
-- Why new: Existing IX_BaseItems_Type_SeriesPresentationUniqueKey_IsFolder_IsVirtua~
--          and IX_BaseItems_Type_SeriesPresentationUniqueKey_PresentationUniqu~
--          both lead with Type.  When Type is not constrained — e.g., the
--          GROUP BY path in GetNextUpSeriesKeys — MySQL cannot use those indexes.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_SeriesPresentationUniqueKey
    ON BaseItems (SeriesPresentationUniqueKey);


-- ---------------------------------------------------------------------------
-- [HIGH] Library scoping without Type — TopParentId + IsVirtualItem + DateCreated
-- Pattern: WHERE TopParentId IN (...) AND IsVirtualItem = 0 ORDER BY DateCreated DESC
-- Source:  TranslateQuery queryTopParentIds path when no IncludeItemTypes is set
-- Why new: IX_BaseItems_TopParentId_Id includes Id as the second column, not
--          IsVirtualItem.  IX_BaseItems_Type_TopParentId_IsVirtualItem_Presentation~
--          leads with Type.  Neither helps a TopParentId-scoped recently-added
--          query with no type filter.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_TopParentId_IsVirtualItem_DateCreated
    ON BaseItems (TopParentId, IsVirtualItem, DateCreated DESC);


-- ---------------------------------------------------------------------------
-- [HIGH] Year / decade filtering
-- Pattern: WHERE ProductionYear IN (...) [AND Type IN (...)]
-- Source:  TranslateQuery: baseQuery.WhereOneOrMany(filter.Years, e => e.ProductionYear!.Value)
-- Why new: No index on ProductionYear exists.  Year filtering is a primary UI
--          control in every client's filter drawer.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_ProductionYear_Type
    ON BaseItems (ProductionYear, Type);


-- ---------------------------------------------------------------------------
-- [HIGH] Community-rating sort
-- Pattern: ORDER BY CommunityRating DESC [WHERE Type = ?]
-- Source:  OrderMapper.MapOrderByField(ItemSortBy.CommunityRating, ...)
-- Why new: No index on CommunityRating exists.  Every "Top Rated" sort on a
--          large library performs a full scan + filesort.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_Type_CommunityRating
    ON BaseItems (Type, CommunityRating DESC);


-- ---------------------------------------------------------------------------
-- [HIGH] CleanName search / exact-name lookup (prefix B-tree index)
-- Pattern: WHERE CleanName = ?             (filter.Name exact match)
--          WHERE CleanName LIKE 'prefix%'  (NameStartsWith)
--          WHERE CleanName LIKE '%term%'   (SearchTerm — partial; still reduces
--                                           scanned rows via index row length)
-- Source:  TranslateQuery filter.Name, filter.NameStartsWith, filter.SearchTerm paths
-- Why new: CleanName is `longtext` — MySQL cannot index longtext without a prefix.
--          A 255-byte prefix index satisfies equality and leading-prefix LIKE.
-- Note:    The FULLTEXT index below is the recommended path for full-text search;
--          this B-tree index handles exact / prefix lookups.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_CleanName_Prefix
    ON BaseItems (CleanName(255));


-- ---------------------------------------------------------------------------
-- [HIGH] Full-text search on Name and OriginalTitle
-- Pattern: WHERE Name LIKE '%term%' OR OriginalTitle LIKE '%term%'
-- Source:  TranslateQuery SearchTerm path (Contains / EF.Functions.Like)
-- Why new: Both Name and OriginalTitle are `longtext`.  B-tree indexes are
--          useless for mid-string LIKE.  A FULLTEXT index enables IN BOOLEAN MODE
--          queries and eliminates full-table scans for search.
-- Note:    EF Core currently emits LIKE SQL.  To exploit FULLTEXT the application
--          needs a MATCH() ... AGAINST() raw-SQL path; this index is pre-positioned
--          for that optimisation without requiring a future schema change.
-- ---------------------------------------------------------------------------
CREATE FULLTEXT INDEX IF NOT EXISTS FT_BaseItems_Name_OriginalTitle
    ON BaseItems (Name, OriginalTitle);


-- ---------------------------------------------------------------------------
-- [MEDIUM] Alphabetical navigation — SortName range scans
-- Pattern: WHERE SortName >= ?  (NameStartsWithOrGreater)
--          WHERE SortName <  ?  (NameLessThan)
--          ORDER BY SortName    (default sort, already covered by first index
--                                when Type+IsVirtualItem are also constrained)
-- Source:  TranslateQuery NameStartsWithOrGreater / NameLessThan,
--          ApplyOrder fallback
-- Why new: SortName appears as the third column in
--          IX_BaseItems_Type_SeriesPresentationUniqueKey_PresentationUniqu~ but
--          only after two equality predicates.  A standalone index serves
--          range-only browsing.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_SortName
    ON BaseItems (SortName);


-- ---------------------------------------------------------------------------
-- [MEDIUM] Premiere date range filtering
-- Pattern: WHERE PremiereDate >= NOW()            (IsUnaired filter)
--          WHERE PremiereDate BETWEEN min AND max  (MinPremiereDate/MaxPremiereDate)
-- Source:  TranslateQuery IsUnaired, MinPremiereDate, MaxPremiereDate
-- Why new: No index on PremiereDate exists.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_Type_PremiereDate
    ON BaseItems (Type, PremiereDate);


-- ---------------------------------------------------------------------------
-- [MEDIUM] IsFolder / IsVirtualItem combined predicate
-- Pattern: WHERE IsFolder = 0 AND IsVirtualItem = 0  (GetIsPlayed recursive path)
-- Source:  BaseItemRepository.GetIsPlayed, TraverseHirachyDown post-filter
-- Why new: Existing indexes with IsVirtualItem always pair it with other columns
--          that may not be present.  A dedicated IsFolder+IsVirtualItem index
--          avoids a partial scan for the folder-exclusion path.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_IsFolder_IsVirtualItem
    ON BaseItems (IsFolder, IsVirtualItem);


-- ---------------------------------------------------------------------------
-- [MEDIUM] DateLastSaved — incremental sync / library refresh detection
-- Pattern: WHERE DateLastSaved >= ?  (MinDateLastSaved / MinDateLastSavedForUser)
-- Source:  TranslateQuery MinDateLastSaved path
-- Why new: No index on DateLastSaved exists.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_DateLastSaved
    ON BaseItems (DateLastSaved);


-- ---------------------------------------------------------------------------
-- [LOW] PrimaryVersionId — alternate-version grouping
-- Pattern: WHERE PrimaryVersionId = ?
-- Source:  Video.PrimaryVersionId alternate-version lookup
-- Why new: PrimaryVersionId is `longtext`; requires a prefix index.
--          The value is a GUID string — 36 bytes is sufficient.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_PrimaryVersionId_Prefix
    ON BaseItems (PrimaryVersionId(36));


-- ---------------------------------------------------------------------------
-- [LOW] ChannelId — Live TV channel scoping
-- Pattern: WHERE ChannelId IN (...)
-- Source:  TranslateQuery filter.ChannelIds path
-- Why new: No standalone index on ChannelId.  ChannelId is char(36) — directly
--          indexable.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_BaseItems_ChannelId
    ON BaseItems (ChannelId);


-- =============================================================================
-- TABLE: UserData
-- =============================================================================
--
-- Existing ItemId-first indexes:
--   IX_UserData_ItemId_UserId_IsFavorite            (ItemId, UserId, IsFavorite)
--   IX_UserData_ItemId_UserId_PlaybackPositionTicks  (ItemId, UserId, PlaybackPositionTicks)
--   IX_UserData_ItemId_UserId_Played                (ItemId, UserId, Played)
--   IX_UserData_ItemId_UserId_LastPlayedDate         (ItemId, UserId, LastPlayedDate)
--
-- These serve point-lookups where ItemId is already known (e.g., loading a
-- specific item's watch state).  They CANNOT serve user-centric home-screen
-- queries that start from a UserId and scan all items in that user's watch
-- history.  The following UserId-first indexes fill that gap.

-- ---------------------------------------------------------------------------
-- [CRITICAL] Favorites shelf
-- Pattern: WHERE UserId = ? AND IsFavorite = 1
-- Source:  TranslateQuery filter.IsFavorite / filter.IsFavoriteOrLiked
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_UserData_UserId_IsFavorite
    ON UserData (UserId, IsFavorite);


-- ---------------------------------------------------------------------------
-- [CRITICAL] Unplayed / played filter — Continue Watching, Mark Played
-- Pattern: WHERE UserId = ? AND Played = 0/1
-- Source:  TranslateQuery filter.IsPlayed path
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_UserData_UserId_Played
    ON UserData (UserId, Played);


-- ---------------------------------------------------------------------------
-- [CRITICAL] Resume / playback position — Continue Watching shelf
-- Pattern: WHERE UserId = ? AND PlaybackPositionTicks > 0
-- Source:  TranslateQuery filter.IsResumable path
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_UserData_UserId_PlaybackPositionTicks
    ON UserData (UserId, PlaybackPositionTicks);


-- ---------------------------------------------------------------------------
-- [HIGH] Last played date — Recently Played, Next Up ordering
-- Pattern: WHERE UserId = ? ORDER BY LastPlayedDate DESC
-- Source:  GetNextUpSeriesKeys MAX(LastPlayedDate), home-screen Continue Watching
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_UserData_UserId_LastPlayedDate
    ON UserData (UserId, LastPlayedDate DESC);


-- =============================================================================
-- TABLE: ItemValuesMap
-- =============================================================================

-- ---------------------------------------------------------------------------
-- [CRITICAL] Genre / studio / tag join — ItemValueId-first covering direction
-- Pattern: The subquery inside WhereReferencedItem runs:
--            context.ItemValuesMap.Any(val => val.ItemValueId == iv.ItemValueId
--                                         && val.ItemId == item.Id)
--          and the GetItemValues aggregation joins:
--            ItemValuesMap m ON m.ItemId = bi.Id
--            ItemValues    v ON v.ItemValueId = m.ItemValueId
-- Existing: PK is (ItemValueId, ItemId) — good for ItemValueId-first joins.
--           IX_ItemValuesMap_ItemId — good for ItemId-first joins.
-- Why new:  A (ItemValueId, ItemId) explicit non-PK index is already implicit
--           in the PK, but an explicit composite ensures it is visible to the
--           optimizer as a covering index for queries that project only those two
--           columns (e.g., the EXISTS subqueries in WhereReferencedItem).
--           In MySQL the PK itself IS a clustered index so this is a secondary
--           confirmation index useful only on engines that separate PK from index;
--           for InnoDB it is technically redundant with the PK.  However the
--           ItemId-first direction (IX_ItemValuesMap_ItemId) does NOT include
--           ItemValueId — adding it here creates a true covering index for
--           ItemId-based EXISTS checks that also need ItemValueId.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_ItemValuesMap_ItemValueId_ItemId
    ON ItemValuesMap (ItemValueId, ItemId);


-- =============================================================================
-- TABLE: PeopleBaseItemMap
-- =============================================================================

-- ---------------------------------------------------------------------------
-- [MEDIUM] Person-to-item join for PersonIds filter
-- Pattern: WHERE PeopleId IN (...) AND ItemId = ?
-- Source:  TranslateQuery PersonIds path:
--            context.PeopleBaseItemMap.Any(m => m.ItemId == e.Id
--                                           && peopleEntityIds.Contains(m.PeopleId))
-- Existing: PK (ItemId, PeopleId, Role) — efficient for ItemId-first lookups.
--           IX_PeopleBaseItemMap_PeopleId — single-column on PeopleId.
-- Why new:  A lookup that filters by PeopleId and needs to confirm ItemId must
--           either traverse the PeopleId index then re-check ItemId against the
--           clustered index, or scan all ItemIds for that person.  A (PeopleId, ItemId)
--           composite avoids the clustered-index lookup for the ItemId confirmation.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_PeopleBaseItemMap_PeopleId_ItemId
    ON PeopleBaseItemMap (PeopleId, ItemId);


-- =============================================================================
-- TABLE: MediaStreamInfos
-- =============================================================================

-- ---------------------------------------------------------------------------
-- [MEDIUM] Language / stream-type filtering for subtitle/audio track queries
-- Pattern: WHERE ItemId = ? AND StreamType = ? AND Language = ?
--          WHERE ItemId = ? AND StreamType = ? AND IsExternal = ? AND Language = ?
-- Source:  TranslateQuery HasNoAudioTrackWithLanguage,
--          HasNoInternalSubtitleTrackWithLanguage, HasNoExternalSubtitleTrackWithLanguage,
--          HasSubtitles paths
-- Existing: PK (ItemId, StreamIndex) — good for per-item stream enumeration.
--           IX_MediaStreamInfos_StreamIndex_StreamType_Language leads with
--           StreamIndex (an ordered integer per item), not with StreamType or
--           Language.  That index is useless for cross-item language filtering.
-- Why new:  (ItemId, StreamType, Language) matches the exact predicate shape:
--           start from the item, narrow to stream type, then filter by language.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_MediaStreamInfos_ItemId_StreamType_Language
    ON MediaStreamInfos (ItemId, StreamType, Language);
