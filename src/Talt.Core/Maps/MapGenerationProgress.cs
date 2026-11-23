namespace Talt.Core.Maps;

/// <summary>Stages of map generation, in order. The client shows localized names (<c>stage.map.*</c>).</summary>
public enum MapGenerationStage
{
    Terrain,
    Slopes,
    Water,
    Rivers,
    Surface,
    Scenery,
    Sites,
    World,
}

/// <summary>Progress of a generation: the stage being worked on and the overall fraction done (0 to 1).</summary>
public readonly record struct MapGenerationProgress(MapGenerationStage Stage, float Fraction);
