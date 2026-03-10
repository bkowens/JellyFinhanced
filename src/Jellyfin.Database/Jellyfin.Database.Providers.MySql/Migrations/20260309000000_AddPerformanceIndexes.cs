using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Providers.MySql.Migrations
{
    /// <summary>
    /// Adds performance-critical indexes identified by deep query-pattern analysis of
    /// BaseItemRepository.TranslateQuery, ApplyOrder, GetItemValues, and GetNextUpSeriesKeys.
    ///
    /// Priority legend used in comments:
    ///   CRITICAL  - missing index causes full table scan on the most common read paths
    ///   HIGH      - significant slowdown at 10 000+ items
    ///   MEDIUM    - noticeable at 50 000+ items
    ///   LOW       - marginal; optimise last
    /// </summary>
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ----------------------------------------------------------------
            // BaseItems -- library-browsing and filtering indexes
            // ----------------------------------------------------------------

            // [CRITICAL] Library browse: WHERE Type IN (...) AND IsVirtualItem = 0 ORDER BY SortName
            // TranslateQuery always filters on Type (IncludeItemTypes / ExcludeItemTypes) and
            // frequently on IsVirtualItem.  ApplyOrder falls back to ORDER BY SortName when no
            // explicit sort is supplied.  A three-column index lets MySQL satisfy the WHERE and
            // avoid a filesort in the common case.
            // SortName is varchar(128) — directly indexable, no prefix needed.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_Type_IsVirtualItem_SortName
                    ON BaseItems (Type, IsVirtualItem, SortName);
                """);

            // [CRITICAL] Parent-folder browsing: WHERE ParentId = ? AND IsVirtualItem = 0
            // Every folder-contents request goes through:
            //   baseQuery.Where(e => e.ParentId!.Value == filter.ParentId)
            // combined with IsVirtualItem filtering.  The existing IX_BaseItems_ParentId is a
            // single-column index that cannot eliminate the IsVirtualItem predicate, forcing a
            // full index scan on large folders.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_ParentId_IsVirtualItem_Type
                    ON BaseItems (ParentId, IsVirtualItem, Type);
                """);

            // [CRITICAL] Recently-added: WHERE Type = ? AND IsVirtualItem = 0 ORDER BY DateCreated DESC
            // Used by the "Latest" row on the home screen and any ItemsController call with
            // sortBy=DateCreated.  Existing indexes with DateCreated include TopParentId in their
            // leading columns, which is not always supplied.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_Type_IsVirtualItem_DateCreated
                    ON BaseItems (Type, IsVirtualItem, DateCreated DESC);
                """);

            // [CRITICAL] Series -> Season -> Episode hierarchy lookups:
            //   WHERE SeriesId = ? (episodes under a series)
            //   WHERE SeasonId = ? (episodes under a season)
            // GetNextUpSeriesKeys, TvShowsController.GetEpisodes, and GetSeasons all rely on these.
            // No index on SeriesId or SeasonId exists in the initial migration.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_SeriesId_IsVirtualItem
                    ON BaseItems (SeriesId, IsVirtualItem);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_SeasonId_IsVirtualItem
                    ON BaseItems (SeasonId, IsVirtualItem);
                """);

            // [HIGH] SeriesPresentationUniqueKey lookups used by GetNextUpSeriesKeys and
            // "Next Up" queries:
            //   WHERE SeriesPresentationUniqueKey = ?
            //   GROUP BY SeriesPresentationUniqueKey
            // The existing composite IX_BaseItems_Type_SeriesPresentationUniqueKey_IsFolder_IsVirtua~
            // leads with Type, which is not always constrained in these paths.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_SeriesPresentationUniqueKey
                    ON BaseItems (SeriesPresentationUniqueKey);
                """);

            // [HIGH] TopParentId with IsVirtualItem — library root scoping without a Type filter.
            // TranslateQuery applies TopParentId when filter.TopParentIds is non-empty.  The
            // existing IX_BaseItems_TopParentId_Id includes Id but not IsVirtualItem, forcing a
            // post-filter step.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_TopParentId_IsVirtualItem_DateCreated
                    ON BaseItems (TopParentId, IsVirtualItem, DateCreated DESC);
                """);

            // [HIGH] Year filtering: WHERE ProductionYear = ? (possibly combined with Type)
            // TranslateQuery calls WhereOneOrMany on filter.Years — no index exists on
            // ProductionYear.  At 50 000 items this is a full table scan.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_ProductionYear_Type
                    ON BaseItems (ProductionYear, Type);
                """);

            // [HIGH] Community-rating sort: ORDER BY CommunityRating DESC (with Type filter)
            // No index on CommunityRating exists.  Every "sort by rating" request on a large
            // library scans all rows for the type.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_Type_CommunityRating
                    ON BaseItems (Type, CommunityRating DESC);
                """);

            // [HIGH] CleanName search / exact-name lookup:
            //   WHERE CleanName LIKE '%term%'        (SearchTerm path)
            //   WHERE CleanName = ?                  (filter.Name path)
            //   WHERE CleanName LIKE 'prefix%'       (NameStartsWith path)
            // CleanName is declared as `longtext` in the DDL.  MySQL cannot index longtext
            // directly.  A prefix index on the first 255 bytes satisfies prefix and equality
            // lookups; range / LIKE '%..%' still scan but benefit from reduced row width.
            // For true full-text search a FULLTEXT index is also added (see below).
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_CleanName_Prefix
                    ON BaseItems (CleanName(255));
                """);

            // [HIGH] FULLTEXT index on Name and OriginalTitle for SearchTerm queries.
            // TranslateQuery performs:
            //   e.CleanName!.Contains(cleanedSearchTerm)
            //   || e.OriginalTitle!.ToLower().Contains(originalSearchTerm)
            // Both columns are longtext, which MySQL cannot index with B-tree indexes.
            // A FULLTEXT index allows IN BOOLEAN MODE queries and eliminates full-table scans
            // for search on libraries with thousands of items.  Note: EF Core will still emit
            // LIKE/Contains SQL; a separate stored procedure or raw-SQL search path is needed
            // to exploit FULLTEXT, but having the index ready costs nothing and enables future
            // optimisation without a schema change.
            migrationBuilder.Sql(
                """
                CREATE FULLTEXT INDEX FT_BaseItems_Name_OriginalTitle
                    ON BaseItems (Name, OriginalTitle);
                """);

            // [MEDIUM] SortName range queries for alphabetical browsing:
            //   WHERE SortName >= ? (NameStartsWithOrGreater)
            //   WHERE SortName < ?  (NameLessThan)
            // SortName already appears as the third column of
            // IX_BaseItems_Type_SeriesPresentationUniqueKey_PresentationUniqu~ but only when
            // SeriesPresentationUniqueKey is also constrained.  A stand-alone index helps the
            // common alphabetical-strip navigation.
            // SortName is varchar(128) — directly indexable.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_SortName
                    ON BaseItems (SortName);
                """);

            // [MEDIUM] PremiereDate filtering / unaired items:
            //   WHERE PremiereDate >= NOW()   (IsUnaired)
            //   WHERE PremiereDate BETWEEN ?  (MinPremiereDate / MaxPremiereDate)
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_Type_PremiereDate
                    ON BaseItems (Type, PremiereDate);
                """);

            // [MEDIUM] IsFolder flag — used by folder-hierarchy traversal and
            // WHERE IsFolder = 0 AND IsVirtualItem = 0 in GetIsPlayed / recursive queries.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_IsFolder_IsVirtualItem
                    ON BaseItems (IsFolder, IsVirtualItem);
                """);

            // [MEDIUM] DateLastSaved — used by MinDateLastSaved / MinDateLastSavedForUser filters
            // during incremental sync / library refresh detection.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_DateLastSaved
                    ON BaseItems (DateLastSaved);
                """);

            // [LOW] PrimaryVersionId — used by video alternate-version grouping.
            // PrimaryVersionId is `longtext`; prefix index required.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_PrimaryVersionId_Prefix
                    ON BaseItems (PrimaryVersionId(36));
                """);

            // [LOW] ChannelId — used by filter.ChannelIds.
            // Already nullable char(36); direct index is fine.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_BaseItems_ChannelId
                    ON BaseItems (ChannelId);
                """);

            // ----------------------------------------------------------------
            // UserData — user-state query indexes
            // ----------------------------------------------------------------

            // [CRITICAL] UserId-first lookup for all user-data subqueries:
            //   WHERE UserId = ?                                (favorite, played, resume)
            //   WHERE UserId = ? AND PlaybackPositionTicks > 0 (resume)
            //   WHERE UserId = ? AND IsFavorite = 1            (favorites)
            //   WHERE UserId = ? AND Played = ?                (unplayed)
            // The existing IX_UserData_UserId is a single-column index, which is correct for
            // the FK index but forces MySQL to scan all rows for that user to apply the second
            // predicate.  Covering composite indexes for the three most frequent patterns are
            // added here.
            //
            // NOTE: The existing ItemId-first indexes
            //   IX_UserData_ItemId_UserId_IsFavorite
            //   IX_UserData_ItemId_UserId_PlaybackPositionTicks
            //   IX_UserData_ItemId_UserId_Played
            //   IX_UserData_ItemId_UserId_LastPlayedDate
            // are correct for point-lookups by ItemId but cannot serve the user-centric
            // scans that dominate home-screen queries (favorites shelf, resume shelf, etc).
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_UserData_UserId_IsFavorite
                    ON UserData (UserId, IsFavorite);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IX_UserData_UserId_Played
                    ON UserData (UserId, Played);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IX_UserData_UserId_PlaybackPositionTicks
                    ON UserData (UserId, PlaybackPositionTicks);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IX_UserData_UserId_LastPlayedDate
                    ON UserData (UserId, LastPlayedDate DESC);
                """);

            // ----------------------------------------------------------------
            // ItemValuesMap — genre/studio/tag join indexes
            // ----------------------------------------------------------------

            // [CRITICAL] Genre/studio/tag filtering joins ItemValuesMap to BaseItems:
            //   JOIN ItemValuesMap m ON m.ItemId = bi.Id
            //   WHERE m.ItemValueId = ?
            // The PK is (ItemValueId, ItemId) so lookups by ItemValueId alone are fast.
            // However the subquery inside WhereReferencedItem / WhereReferencedItemMultipleTypes
            // runs:
            //   context.ItemValuesMap.Any(val => val.map.ItemId == item.Id)
            // which is an ItemId-first lookup.  IX_ItemValuesMap_ItemId already covers this.
            // What is missing is a composite index that also includes ItemValueId so that the
            // join from ItemValues side can be satisfied without touching the base table.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_ItemValuesMap_ItemValueId_ItemId
                    ON ItemValuesMap (ItemValueId, ItemId);
                """);

            // ----------------------------------------------------------------
            // AncestorIds — collection / ancestor-chain queries
            // ----------------------------------------------------------------

            // [HIGH] Collection membership queries:
            //   WHERE e.Parents.Any(a => ancestorIds.Contains(a.ParentItemId))
            // translates to:
            //   EXISTS (SELECT 1 FROM AncestorIds WHERE ItemId = bi.Id AND ParentItemId IN (...))
            // PK is (ItemId, ParentItemId) so the ItemId-first lookup is already covered.
            // The reverse direction — "find all descendants of a given ancestor" — uses
            //   WHERE ParentItemId = ?
            // IX_AncestorIds_ParentItemId exists for that direction.  No gaps here; documenting
            // confirmation that existing indexes are sufficient for this table.

            // ----------------------------------------------------------------
            // PeopleBaseItemMap — person / cast filtering
            // ----------------------------------------------------------------

            // [MEDIUM] Person-ID to People.Id join for PersonIds filter:
            //   context.PeopleBaseItemMap.Any(m => m.ItemId == e.Id && peopleEntityIds.Contains(m.PeopleId))
            // PK is (ItemId, PeopleId, Role).  PeopleId-only lookups use IX_PeopleBaseItemMap_PeopleId.
            // What is missing: when filtering by PeopleId AND ItemId together the PK scan
            // direction is correct, but an explicit composite on (PeopleId, ItemId) avoids
            // scanning all roles for a person.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_PeopleBaseItemMap_PeopleId_ItemId
                    ON PeopleBaseItemMap (PeopleId, ItemId);
                """);

            // ----------------------------------------------------------------
            // MediaStreamInfos — subtitle / audio track language queries
            // ----------------------------------------------------------------

            // [MEDIUM] Language-based subtitle queries:
            //   WHERE StreamType = Audio/Subtitle AND Language = ?
            //   WHERE StreamType = Subtitle AND IsExternal = ? AND Language = ?
            // Existing indexes lead with StreamIndex, which is almost never in the WHERE clause
            // for these filter paths.  A (StreamType, Language) index matches the query shape.
            migrationBuilder.Sql(
                """
                CREATE INDEX IX_MediaStreamInfos_ItemId_StreamType_Language
                    ON MediaStreamInfos (ItemId, StreamType, Language);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // BaseItems indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_Type_IsVirtualItem_SortName ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_ParentId_IsVirtualItem_Type ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_Type_IsVirtualItem_DateCreated ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_SeriesId_IsVirtualItem ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_SeasonId_IsVirtualItem ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_SeriesPresentationUniqueKey ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_TopParentId_IsVirtualItem_DateCreated ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_ProductionYear_Type ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_Type_CommunityRating ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_CleanName_Prefix ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS FT_BaseItems_Name_OriginalTitle ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_SortName ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_Type_PremiereDate ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_IsFolder_IsVirtualItem ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_DateLastSaved ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_PrimaryVersionId_Prefix ON BaseItems;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_BaseItems_ChannelId ON BaseItems;");

            // UserData indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_UserData_UserId_IsFavorite ON UserData;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_UserData_UserId_Played ON UserData;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_UserData_UserId_PlaybackPositionTicks ON UserData;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_UserData_UserId_LastPlayedDate ON UserData;");

            // ItemValuesMap indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_ItemValuesMap_ItemValueId_ItemId ON ItemValuesMap;");

            // PeopleBaseItemMap indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PeopleBaseItemMap_PeopleId_ItemId ON PeopleBaseItemMap;");

            // MediaStreamInfos indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_MediaStreamInfos_ItemId_StreamType_Language ON MediaStreamInfos;");
        }
    }
}
