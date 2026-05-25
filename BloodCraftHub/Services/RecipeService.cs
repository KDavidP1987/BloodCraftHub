using System;
using BloodCraftHub.Utils;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace BloodCraftHub.Services;

// 0.16: client-side "custom recipes" support, ported from Eclipse.
//
// PORT FROM: LearningMods/Eclipse-main/Utilities/Recipes.cs (ModifyRecipes).
//
// Bloodcraft's server enables a set of extra crafting recipes (vampiric dust,
// copper wires, silver ingot, soul-shard extraction, primal jewel, etc.). The
// server broadcasts only a single boolean `extraRecipes` flag in its Eclipse
// ConfigsToClient payload (field 7) — NOT the recipe definitions. Both Eclipse
// and Bloodcraft carry an identical hard-coded recipe table and apply it
// locally so the recipes show up in the in-game crafting/refinement station
// UIs. This is the BloodCraftHub equivalent so BCH can act as a drop-in
// replacement for Eclipse's recipe surface.
//
// IMPORTANT — coexistence: if the standalone Eclipse mod is ALSO installed it
// applies these same prefab/buffer mutations itself. Applying them twice would
// double-inject station recipes / duplicate requirement buffers, so the caller
// gates this on !EclipseProtocolService.IsEclipseModLoaded().
//
// IMPORTANT — this is the only BCH code that MUTATES the game's ECS data
// (BCH is otherwise read-only). It runs entirely client-side and only affects
// what the local crafting UI shows; the server still validates real crafts.
// It is version-coupled to Bloodcraft's recipe set (the GUIDs below mirror
// Bloodcraft v1.13.x). Needs in-game verification.
//
// COSMETIC OMISSION vs Eclipse: Eclipse also renames the jewel-template item to
// "Primal Jewel" and swaps its icon, using its own embedded sprite cache +
// Localization access (CanvasService.DataHUD.Sprites). BCH has no equivalent
// sprite cache, so that purely-cosmetic rename/icon is skipped — the recipe
// itself still works; the crafted jewel just keeps its default template name.
public static class RecipeService
{
    private static bool _applied;

    // Stations.
    static readonly PrefabGUID _advancedGrinder = new(-178579946); // vampiric dust
    static readonly PrefabGUID _fabricator      = new(-465055967); // copper wires, iron body

    // Recipes.
    static readonly PrefabGUID _vampiricDustRecipe  = new(311920560);
    static readonly PrefabGUID _copperWiresRecipe   = new(-2031309726);
    static readonly PrefabGUID _chargedBatteryRecipe = new(-40415372);

    // Ingredients / items.
    static readonly PrefabGUID _batHide        = new(1262845777);
    static readonly PrefabGUID _lesserStygian  = new(2103989354);
    static readonly PrefabGUID _bloodEssence   = new(862477668);
    static readonly PrefabGUID _batteryCharge  = new(-77555820);
    static readonly PrefabGUID _techScrap      = new(834864259);
    static readonly PrefabGUID _primalEssence  = new(1566989408);
    static readonly PrefabGUID _copperWires    = new(-456161884);
    static readonly PrefabGUID _itemBuildingEMP = new(-1447213995);
    static readonly PrefabGUID _depletedBattery = new(1270271716);
    static readonly PrefabGUID _itemJewelTemplate = new(1075994038);

    static readonly PrefabGUID _radiantFibre = new(-182923609);

    static readonly PrefabGUID _goldOre     = new(660533034);
    static readonly PrefabGUID _goldJewelry = new(-1749304196);

    static readonly PrefabGUID _extractShardRecipe   = new(1743327679);
    static readonly PrefabGUID _solarusShardRecipe   = new(-958598508);
    static readonly PrefabGUID _monsterShardRecipe   = new(1791150988);
    static readonly PrefabGUID _manticoreShardRecipe = new(-111826090);
    static readonly PrefabGUID _draculaShardRecipe   = new(-414358988);

    static readonly PrefabGUID _solarusShard   = new(-21943750);
    static readonly PrefabGUID _monsterShard   = new(-1581189572);
    static readonly PrefabGUID _manticoreShard = new(-1260254082);
    static readonly PrefabGUID _draculaShard   = new(666638454);

    static readonly PrefabGUID _primalStygianRecipe = new(-259193408);
    static readonly PrefabGUID _greaterStygian = new(576389135); // salvage for lesser stygian shards
    static readonly PrefabGUID _primalStygian  = new(28358550);

    static readonly PrefabGUID _bloodCrystalRecipe = new(-597461125);
    static readonly PrefabGUID _crystal       = new(-257494203);
    static readonly PrefabGUID _bloodCrystal  = new(-1913156733);
    static readonly PrefabGUID _greaterEssence = new(271594022); // salvage for normal blood essence

    // PrefabGUIDs.* constants from the community prefab table (not in our
    // reference assemblies) — inlined as raw hashes (values from Eclipse's +
    // Bloodcraft's Resources/PrefabGUIDs.cs).
    static readonly PrefabGUID _recipeCastleUpkeepT02 = new(-1281672171);
    static readonly PrefabGUID _ingredientGemdust     = new(820932258);
    static readonly PrefabGUID _ingredientPlantFiber  = new(-1409142667);
    static readonly PrefabGUID _ingredientPollen      = new(855691699);

    static readonly PrefabGUID[] _shardRecipes =
    {
        new(-958598508), // solarus
        new(1791150988), // monster
        new(-111826090), // manticore
        new(-414358988), // dracula
    };
    static PrefabGUID ShardForRecipe(PrefabGUID recipe)
    {
        if (recipe.Equals(_solarusShardRecipe))   return _solarusShard;
        if (recipe.Equals(_monsterShardRecipe))   return _monsterShard;
        if (recipe.Equals(_manticoreShardRecipe)) return _manticoreShard;
        return _draculaShard;
    }

    /// <summary>Apply the custom-recipe modifications once. Safe to call
    /// repeatedly — only the first call does work. Caller is responsible for
    /// the extraRecipes / Eclipse-coexistence / setting gates.</summary>
    public static void ApplyOnce(PrefabGUID primalCost)
    {
        if (_applied) return;
        if (Plugin.IsClientNull()) return;

        var world = Plugin.EntityManager.World;
        var prefabSys = world?.GetExistingSystemManaged<PrefabCollectionSystem>();
        var gameDataSys = world?.GetExistingSystemManaged<GameDataSystem>();
        if (prefabSys == null || gameDataSys == null)
        {
            // Systems not built yet — leave _applied false so a later config
            // broadcast retries (config arrives well after game-data load, so in
            // practice they're ready on the first attempt).
            LogUtils.LogWarning("RecipeService: prefab/gameData system not ready — skipping custom recipes this pass.");
            return;
        }

        // Mark applied BEFORE mutating so a mid-way exception can't cause a
        // retry to double-inject station recipes / duplicate requirement buffers.
        _applied = true;
        try
        {
            ModifyRecipes(prefabSys, gameDataSys, primalCost);
            LogUtils.LogInfo("RecipeService: applied Bloodcraft custom recipes to the local crafting UI.");
        }
        catch (Exception ex)
        {
            // Never let a recipe-table tweak break startup/config handling.
            LogUtils.LogWarning($"RecipeService: failed to apply custom recipes: {ex}");
        }
    }

    private static void ModifyRecipes(PrefabCollectionSystem prefabSys, GameDataSystem gameDataSys, PrefabGUID primalCost)
    {
        var em = Plugin.EntityManager;
        var recipeMap = gameDataSys.RecipeHashLookupMap;
        var prefabMap = prefabSys._PrefabGuidToEntityMap;

        Entity itemEntity;
        Entity recipeEntity;
        Entity prefabEntity;

        // ── itemBuildingEMP: salvage into depleted battery + tech scrap ──
        if (prefabMap.TryGetValue(_itemBuildingEMP, out itemEntity))
        {
            var buf = em.AddBuffer<RecipeRequirementBuffer>(itemEntity);
            buf.Add(new RecipeRequirementBuffer { Guid = _depletedBattery, Amount = 2 });
            buf.Add(new RecipeRequirementBuffer { Guid = _techScrap, Amount = 15 });
            SetSalvage(em, itemEntity, PrefabGUID.Empty, 1f, 20f, onlyIfAbsent: true);
        }

        // ── greaterStygian / greaterEssence: salvageable into castle-upkeep ──
        if (prefabMap.TryGetValue(_greaterStygian, out itemEntity))
            SetSalvage(em, itemEntity, _recipeCastleUpkeepT02, 1f, 10f, onlyIfAbsent: true);
        if (prefabMap.TryGetValue(_greaterEssence, out itemEntity))
            SetSalvage(em, itemEntity, _recipeCastleUpkeepT02, 1f, 5f, onlyIfAbsent: true);

        // ── primal stygian recipe: 8 greater stygian -> 1 primal stygian ──
        if (prefabMap.TryGetValue(_primalStygianRecipe, out recipeEntity))
        {
            var req = em.GetBuffer<RecipeRequirementBuffer>(recipeEntity);
            var r0 = req[0]; r0.Guid = _greaterStygian; r0.Amount = 8; req[0] = r0;

            var outp = em.GetBuffer<RecipeOutputBuffer>(recipeEntity);
            var o0 = outp[0]; o0.Guid = _primalStygian; o0.Amount = 1; outp[0] = o0;

            SetRecipeData(recipeEntity, craftDuration: 10f, alwaysUnlocked: true);
            recipeMap[_primalStygianRecipe] = recipeEntity.Read<RecipeData>();
        }

        // ── blood crystal recipe: 100 crystal + 1 greater essence -> 100 blood crystal ──
        if (prefabMap.TryGetValue(_bloodCrystalRecipe, out recipeEntity))
        {
            var req = em.GetBuffer<RecipeRequirementBuffer>(recipeEntity);
            var r0 = req[0]; r0.Guid = _crystal; r0.Amount = 100; req[0] = r0;
            req.Add(new RecipeRequirementBuffer { Guid = _greaterEssence, Amount = 1 });

            var outp = em.GetBuffer<RecipeOutputBuffer>(recipeEntity);
            var o0 = outp[0]; o0.Guid = _bloodCrystal; o0.Amount = 100; outp[0] = o0;

            SetRecipeData(recipeEntity, craftDuration: 10f, alwaysUnlocked: true);
            recipeMap[_bloodCrystalRecipe] = recipeEntity.Read<RecipeData>();
        }

        // (Eclipse renames/re-icons the jewel template here — cosmetic, skipped.)

        // ── primal essence: salvageable + 5 battery charge ──
        if (prefabMap.TryGetValue(_primalEssence, out prefabEntity))
        {
            SetSalvage(em, prefabEntity, PrefabGUID.Empty, 1f, 10f, onlyIfAbsent: false);
            AddReq(em, prefabEntity, _batteryCharge, 5);
        }

        // ── copper wires: salvageable + 1 battery charge ──
        if (prefabMap.TryGetValue(_copperWires, out prefabEntity))
        {
            SetSalvage(em, prefabEntity, PrefabGUID.Empty, 1f, 15f, onlyIfAbsent: false);
            AddReq(em, prefabEntity, _batteryCharge, 1);
        }

        // ── bat hide: salvageable + lesser stygian + blood essence ──
        if (prefabMap.TryGetValue(_batHide, out prefabEntity))
        {
            SetSalvage(em, prefabEntity, PrefabGUID.Empty, 1f, 15f, onlyIfAbsent: false);
            var buf = EnsureReqBuffer(em, prefabEntity);
            buf.Add(new RecipeRequirementBuffer { Guid = _lesserStygian, Amount = 3 });
            buf.Add(new RecipeRequirementBuffer { Guid = _bloodEssence, Amount = 5 });
        }

        // ── gold ore: salvageable + 2 gold jewelry ──
        if (prefabMap.TryGetValue(_goldOre, out prefabEntity))
        {
            SetSalvage(em, prefabEntity, PrefabGUID.Empty, 1f, 10f, onlyIfAbsent: false);
            AddReq(em, prefabEntity, _goldJewelry, 2);
        }

        // ── radiant fibre: salvageable + gemdust/plant fiber/pollen ──
        if (prefabMap.TryGetValue(_radiantFibre, out prefabEntity))
        {
            SetSalvage(em, prefabEntity, PrefabGUID.Empty, 1f, 10f, onlyIfAbsent: false);
            var buf = EnsureReqBuffer(em, prefabEntity);
            buf.Add(new RecipeRequirementBuffer { Guid = _ingredientGemdust, Amount = 8 });
            buf.Add(new RecipeRequirementBuffer { Guid = _ingredientPlantFiber, Amount = 16 });
            buf.Add(new RecipeRequirementBuffer { Guid = _ingredientPollen, Amount = 24 });
        }

        // ── primal-cost-gated: shard extraction + jewel + per-shard costs ──
        if (primalCost.GuidHash != 0
            && prefabMap.TryGetValue(primalCost, out Entity costEntity)
            && costEntity.Has<ItemData>())
        {
            if (prefabMap.TryGetValue(_extractShardRecipe, out recipeEntity))
            {
                var req = em.GetBuffer<RecipeRequirementBuffer>(recipeEntity);
                var r0 = req[0]; r0.Guid = primalCost; req[0] = r0;

                var outp = em.GetBuffer<RecipeOutputBuffer>(recipeEntity);
                outp.Add(new RecipeOutputBuffer { Guid = _itemJewelTemplate, Amount = 1 });
            }

            foreach (PrefabGUID shardRecipe in _shardRecipes)
            {
                if (!prefabMap.TryGetValue(shardRecipe, out recipeEntity)) continue;
                var buf = em.GetBuffer<RecipeRequirementBuffer>(recipeEntity);
                buf.Add(new RecipeRequirementBuffer { Guid = ShardForRecipe(shardRecipe), Amount = 1 });
                buf.Add(new RecipeRequirementBuffer { Guid = primalCost, Amount = 1 });
            }
        }

        // ── battery charge: strip salvage + requirement buffer ──
        if (prefabMap.TryGetValue(_batteryCharge, out prefabEntity))
        {
            if (prefabEntity.Has<Salvageable>()) RemoveComp<Salvageable>(prefabEntity);
            if (prefabEntity.Has<RecipeRequirementBuffer>()) RemoveComp<RecipeRequirementBuffer>(prefabEntity);
        }

        // ── advanced grinder station: add vampiric dust recipe ──
        if (prefabMap.TryGetValue(_advancedGrinder, out Entity grinder)
            && prefabMap.TryGetValue(_vampiricDustRecipe, out recipeEntity))
        {
            SetRecipeData(recipeEntity, alwaysUnlocked: true);
            recipeMap[_vampiricDustRecipe] = recipeEntity.Read<RecipeData>();

            var refine = em.GetBuffer<RefinementstationRecipesBuffer>(grinder);
            refine.Add(new RefinementstationRecipesBuffer { RecipeGuid = _vampiricDustRecipe, Disabled = false, Unlocked = true });
        }

        // ── fabricator station: add copper wires + charged battery recipes ──
        if (prefabMap.TryGetValue(_fabricator, out Entity fabricator))
        {
            if (prefabMap.TryGetValue(_copperWiresRecipe, out recipeEntity))
            {
                SetRecipeData(recipeEntity, craftDuration: 10f, alwaysUnlocked: true);
                recipeMap[_copperWiresRecipe] = recipeEntity.Read<RecipeData>();
            }

            if (prefabMap.TryGetValue(_chargedBatteryRecipe, out recipeEntity))
            {
                AddReq(em, recipeEntity, _batteryCharge, 1);
                SetRecipeData(recipeEntity, craftDuration: 90f, alwaysUnlocked: true);
                recipeMap[_chargedBatteryRecipe] = recipeEntity.Read<RecipeData>();
            }

            var refine = em.GetBuffer<RefinementstationRecipesBuffer>(fabricator);
            refine.Add(new RefinementstationRecipesBuffer { RecipeGuid = _copperWiresRecipe, Disabled = false, Unlocked = true });
            refine.Add(new RefinementstationRecipesBuffer { RecipeGuid = _chargedBatteryRecipe, Disabled = false, Unlocked = true });
        }
    }

    // ---------- helpers ----------

    // Read-modify-write RecipeData (replaces Eclipse's entity.With(ref RecipeData)).
    private static void SetRecipeData(Entity recipeEntity, float craftDuration = -1f, bool alwaysUnlocked = true)
    {
        var rd = recipeEntity.Read<RecipeData>();
        if (craftDuration >= 0f) rd.CraftDuration = craftDuration;
        rd.AlwaysUnlocked  = alwaysUnlocked;
        rd.HideInStation   = false;
        rd.HudSortingOrder = 0;
        recipeEntity.Write(rd);
    }

    // Add (or set) a Salvageable component (replaces Eclipse's AddWith pattern).
    // onlyIfAbsent=true mirrors Eclipse's `if (!Has<Salvageable>()) { AddWith(...) }`
    // cases — leave an existing Salvageable untouched. false mirrors its
    // `if (!Has) Add; then set` cases — always (re)set the values.
    private static void SetSalvage(EntityManager em, Entity entity, PrefabGUID recipeGuid, float factor, float timer, bool onlyIfAbsent)
    {
        bool present = entity.Has<Salvageable>();
        if (onlyIfAbsent && present) return;
        if (!present) AddComp<Salvageable>(entity);
        entity.Write(new Salvageable
        {
            RecipeGUID    = recipeGuid,
            SalvageFactor = factor,
            SalvageTimer  = timer,
        });
    }

    private static DynamicBuffer<RecipeRequirementBuffer> EnsureReqBuffer(EntityManager em, Entity entity)
    {
        if (!entity.Has<RecipeRequirementBuffer>())
            return em.AddBuffer<RecipeRequirementBuffer>(entity);
        return em.GetBuffer<RecipeRequirementBuffer>(entity);
    }

    private static void AddReq(EntityManager em, Entity entity, PrefabGUID guid, int amount)
    {
        var buf = EnsureReqBuffer(em, entity);
        buf.Add(new RecipeRequirementBuffer { Guid = guid, Amount = amount });
    }

    // Component add/remove via the ComponentType form (BCH's proven IL2CPP
    // pattern — see Utils/Extensions.Has<T>).
    private static void AddComp<T>(Entity e)
        => Plugin.EntityManager.AddComponent(e, new ComponentType(Il2CppType.Of<T>()));
    private static void RemoveComp<T>(Entity e)
        => Plugin.EntityManager.RemoveComponent(e, new ComponentType(Il2CppType.Of<T>()));
}
