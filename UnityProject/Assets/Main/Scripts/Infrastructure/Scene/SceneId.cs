// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Scene
{
    /// <summary>
    /// Logical scene ids. MUST stay identical to the scene_id column in
    /// data/pond.csv (the village pond lives in scene 4, FarmPlot — that is
    /// why the fishing spot is on the farm, not in the village).
    /// Stage 7 mapping (spec): 1=Village, 2=Shop, 3=AuntieHouse, 4=FarmPlot.
    /// </summary>
    public enum SceneId
    {
        /// <summary>Starting outdoor scene with the pond exit to the farm.</summary>
        Village = 1,
        /// <summary>Shopkeeper interior (buy_item_v1).</summary>
        Shop = 2,
        /// <summary>Auntie's house: quests + kitchen (craft_food_v1).</summary>
        AuntieHouse = 3,
        /// <summary>Farm plots + the village pond's fishing spot (fishing_v1).</summary>
        FarmPlot = 4,
    }

    /// <summary>SceneId → Unity .unity asset name (build-settings order 1..4;
    /// index 0 is Persistent, which has no SceneId — it is never unloaded).</summary>
    public static class SceneIdExtensions
    {
        public static string ToSceneName(this SceneId sceneId) => sceneId switch
        {
            SceneId.Village => "Village",
            SceneId.Shop => "Shop",
            SceneId.AuntieHouse => "AuntieHouse",
            SceneId.FarmPlot => "FarmPlot",
            _ => throw new System.ArgumentOutOfRangeException(
                nameof(sceneId), sceneId, "No scene name mapped for this SceneId."),
        };
    }
}
