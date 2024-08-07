using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Genelib {
    // Would like to reuse more code from AiTaskSeekFoodAndEat, but almost all its members are private
    public class AiTaskForage : AiTaskSeekFoodAndEat {
        protected AnimalHunger hungerBehavior;
        protected double lastSearchHours;
        protected float searchRate = 0.25f;
        protected float looseItemSearchDistance = 10;
        protected float motherSearchDistance = 12;
        protected POIRegistry pointsOfInterest;
        protected AnimationMetaData digAnimation;
        protected AnimationMetaData eatAnimation;
        protected GrazeMethod grazeMethod;
        protected string[] nurseFromEntities;

        protected IAnimalFoodSource Target {
            get => (IAnimalFoodSource) typeof(AiTaskSeekFoodAndEat).GetField("targetPoi", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this);
            set => typeof(AiTaskSeekFoodAndEat).GetField("targetPoi", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(this, value);
        }

        protected AnimationMetaData CurrentEatAnimation {
            get => (AnimationMetaData) typeof(AiTaskSeekFoodAndEat).GetField("eatAnimMeta", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this);
            set => typeof(AiTaskSeekFoodAndEat).GetField("eatAnimMeta", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(this, value);
        }

        public AiTaskForage(EntityAgent entity) : base(entity) { 
            pointsOfInterest = entity.Api.ModLoader.GetModSystem<POIRegistry>();
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig) {
            base.LoadConfig(taskConfig, aiConfig);
            lastSearchHours = entity.World.Calendar.TotalHours - searchRate * entity.World.Rand.NextSingle();
            if (taskConfig["digAnimation"].Exists) {
                string animation = taskConfig["digAnimation"].AsString()?.ToLowerInvariant();
                digAnimation = new AnimationMetaData() {
                    Code = animation,
                    Animation = animation,
                    AnimationSpeed = taskConfig["digAnimationSpeed"].AsFloat(1f)
                }.Init();
            }
            eatAnimation = CurrentEatAnimation;
            if (taskConfig["nurseFromEntities"].Exists) {
                nurseFromEntities = taskConfig["nurseFromEntities"].AsArray<string>();
            }
        }

        public override void AfterInitialize() {
            hungerBehavior = entity.GetBehavior<AnimalHunger>();
        }

        public override bool ShouldExecute() {
            if (!IsSearchTime()) {
                return false;
            }
            Target = null;
            CurrentEatAnimation = eatAnimation;

            float foodLevel = hungerBehavior.Saturation / hungerBehavior.AdjustedMaxSaturation;
            if (foodLevel < AnimalHunger.HUNGRY) {
                if (hungerBehavior.WantsMilk()) {
                    SeekMilk();
                    if (Target != null) {
                        return true;
                    }
                }
                if (hungerBehavior.StartedWeaning()) {
                    // Eat loose items
                    entity.Api.ModLoader.GetModSystem<EntityPartitioning>().WalkEntities(
                        entity.ServerPos.XYZ, looseItemSearchDistance, searchItems, EnumEntitySearchType.Inanimate);
                    if (Target != null) {
                        return true;
                    }
                }
            }

            float thirstLevel = hungerBehavior.Water.Level;
            if (thirstLevel < foodLevel && thirstLevel < 0) {
                SeekWater();
                if (Target != null) {
                    return true;
                }
            }
            if (foodLevel < AnimalHunger.HUNGRY) {
                if (hungerBehavior.StartedWeaning()) {
                    SeekFood();
                    if (Target != null) {
                        return true;
                    }
                }
                if (hungerBehavior.CanDigestMilk()) {
                    SeekMilk();
                }
            }
            if (Target == null) {
                lastSearchHours = entity.World.Calendar.TotalHours;
            }
            return Target != null;
        }

        public override void FinishExecute(bool cancelled) {
            // Base method resets cooldown if quantityEaten is 0
            base.FinishExecute(cancelled);
            if (!cancelled) {
                cooldownUntilTotalHours = entity.Api.World.Calendar.TotalHours + mincooldownHours + entity.World.Rand.NextDouble() * (maxcooldownHours - mincooldownHours);
            }
        }

        private bool searchItems(Entity entity) {
            if (entity is EntityItem entityitem && hungerBehavior.WantsFood(entityitem.Itemstack)) {
                Target = new LooseItemFoodSource(entityitem);
                return false;
            }
            return true;
        }

        private bool searchMother(Entity entity) {
            if (entity.EntityId == this.entity.WatchedAttributes.GetLong("motherId")
                    || entity.EntityId == this.entity.WatchedAttributes.GetLong("fosterId")) {
                Target = new NursingMilkSource(entity);
                return false;
            }
            return true;
        }

        private bool searchFoster(Entity entity) {
            foreach (string nurseFrom in nurseFromEntities) {
                if (entity.WildCardMatch(AssetLocation.Create(nurseFrom, this.entity.Code.Domain))) {
                    Target = new NursingMilkSource(entity);
                    this.entity.WatchedAttributes.SetLong("fosterId", entity.EntityId);
                    return false;
                }
            }
            return true;
        }

        protected bool IsSearchTime() {
            return lastSearchHours + searchRate <= entity.World.Calendar.TotalHours
                && cooldownUntilMs <= entity.World.ElapsedMilliseconds
                && cooldownUntilTotalHours <= entity.World.Calendar.TotalHours
                && EmotionStatesSatisifed();
        }

        protected void SeekMilk() {
            entity.Api.ModLoader.GetModSystem<EntityPartitioning>().WalkEntities(
                entity.ServerPos.XYZ, motherSearchDistance, searchMother, EnumEntitySearchType.Creatures);
            if (Target == null && !hungerBehavior.StartedWeaning() && nurseFromEntities != null) {
                entity.Api.ModLoader.GetModSystem<EntityPartitioning>().WalkEntities(
                    entity.ServerPos.XYZ, motherSearchDistance, searchFoster, EnumEntitySearchType.Creatures);
            }
        }

        protected void SeekFood() {
            Target = pointsOfInterest.GetNearestPoi(entity.ServerPos.XYZ, 48, IsValidFoodPOI) as IAnimalFoodSource;
            if (Target != null) {
                return;
            }
            if (hungerBehavior.EatsGrassOrRoots()) {
                grazeMethod = hungerBehavior.GetGrazeMethod(entity.World.Rand);
                var grass = GrassFoodSource.SearchNear(entity);
                if (grass.IsSuitableFor(entity, grazeMethod) && !RecentlyFailedSeek(grass)) {
                    Target = grass;
                    if (grazeMethod == GrazeMethod.Root) {
                        CurrentEatAnimation = digAnimation;
                    }
                    return;
                }
            }
            // TODO: Maybe deer should browse on leaves?
        }

        protected void SeekWater() {
            // TODO
        }

        protected bool IsValidFoodPOI(IPointOfInterest poi) {
            if (poi.Type != "food") {
                return false;
            }
            IAnimalFoodSource foodSource = poi as IAnimalFoodSource;
            if (foodSource == null) {
                return false;
            }
            if (!foodSource.IsSuitableFor(entity, Diet)) {
                return false;
            }
            if (RecentlyFailedSeek(poi)) {
                return false;
            }
            return true;
        }

        protected bool RecentlyFailedSeek(IPointOfInterest poi) {
            FieldInfo fieldInfo = typeof(AiTaskSeekFoodAndEat).GetField("failedSeekTargets", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary dict = (IDictionary)fieldInfo.GetValue(this);
            object failedSeek = dict[poi];
            if (failedSeek == null) {
                return false;
            }
            int count = (int) failedSeek.GetType().GetField("Count", BindingFlags.Public | BindingFlags.Instance).GetValue(failedSeek);
            if (count < 4) {
                return false;
            }
            long lastTryMs = (long) failedSeek.GetType().GetField("LastTryMs", BindingFlags.Public | BindingFlags.Instance).GetValue(failedSeek);
            return lastTryMs >= entity.World.ElapsedMilliseconds - 60000;
        }
    }
}
