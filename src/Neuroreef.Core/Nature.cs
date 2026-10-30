// The strength of nature (the setup screen's Nature group): multipliers on the designed laws of plants, seasons,
// weather, fire, disease and water, chosen once at startup with the map. 1 = the documented constants
// (CONSTANTS.md); the person running the world chooses how harsh their nature is. Saved in checkpoints.

namespace Neuroreef;

public static class Nature
{
    public static float Plants = 1f;          // x every plant and algae growth rate (lush > 1, harsh < 1)
    public static float Seasons = 1f;         // x A_SEASON: how much wetter the wet season and drier the dry one
    public static int SeasonLength = K.T_SEASON;   // steps per year
    public static float Drought = 1f;         // x RAIN_SD: how far a year's rainfall can swing (droughts and wet years)
    public static float Fire = 1f;            // x LAMBDA_FIRE: how often fires start
    public static float Disease = 1f;         // x PG_INF: how easily pathogens infect (0 = no disease)
    public static float Thirst = 1f;          // x C_THIRST: how fast animals dry out on land
    public static float Water = K.LAKE_SHARE; // share of the map under water
    public static float Ponds = 1f;           // share of land basins that hold seasonal ponds (0 = water only in lakes and rivers)
    public static float Climate = K.CLIMATE;  // how strongly sun, height and seasons change temperature (Revision O; 0 = everywhere T_REF, no heat budget)
    public static int WorldMap = K.WORLD;     // the map side of the world that follows a prehistory (it may be smaller: a valley)

    public static void Clamp()
    {
        Plants = System.Math.Clamp(Plants, 0.1f, 4f); Seasons = System.Math.Clamp(Seasons, 0f, 2.2f);
        SeasonLength = System.Math.Clamp(SeasonLength, 1000, 100000); Drought = System.Math.Clamp(Drought, 0f, 4f);
        Fire = System.Math.Clamp(Fire, 0f, 10f); Disease = System.Math.Clamp(Disease, 0f, 5f);
        Thirst = System.Math.Clamp(Thirst, 0.1f, 4f); Water = System.Math.Clamp(Water, 0.02f, 0.6f);
        Ponds = System.Math.Clamp(Ponds, 0f, 1f); Climate = System.Math.Clamp(Climate, 0f, 2f);
    }

    // for checkpoints and saved worlds: the map side and every setting, in a fixed order
    public static void Write(System.IO.BinaryWriter bw)
    {
        bw.Write(Map.W); bw.Write(Plants); bw.Write(Seasons); bw.Write(SeasonLength); bw.Write(Drought);
        bw.Write(Fire); bw.Write(Disease); bw.Write(Thirst); bw.Write(Water); bw.Write(Ponds); bw.Write(WorldMap); bw.Write(Climate);
    }

    /// <summary>Read the settings a save was made with and apply them (before any World is built).</summary>
    public static void Read(System.IO.BinaryReader br)
    {
        Map.Set(br.ReadInt32()); Plants = br.ReadSingle(); Seasons = br.ReadSingle(); SeasonLength = br.ReadInt32();
        Drought = br.ReadSingle(); Fire = br.ReadSingle(); Disease = br.ReadSingle(); Thirst = br.ReadSingle(); Water = br.ReadSingle(); Ponds = br.ReadSingle(); WorldMap = br.ReadInt32(); Climate = br.ReadSingle();
    }

    public static string Describe() =>
        $"{Map.Name} map ({Map.W} units), plants x{Plants:0.##}, seasons x{Seasons:0.##} over {SeasonLength:N0} steps, " +
        $"droughts x{Drought:0.##}, fire x{Fire:0.##}, disease x{Disease:0.##}, thirst x{Thirst:0.##}, water {Water:P0}, ponds {Ponds:P0}, climate x{Climate:0.##}";
}
