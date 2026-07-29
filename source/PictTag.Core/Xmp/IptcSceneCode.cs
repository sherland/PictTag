namespace PictTag.Core.Xmp;

/// <summary>
/// Maps <see cref="SceneType"/> to its 6-digit code in the IPTC Scene-NewsCodes controlled
/// vocabulary (https://cv.iptc.org/newscodes/scene), used by Iptc4xmpCore:Scene.
/// </summary>
internal static class IptcSceneCode
{
    public static string ForSceneType(SceneType scene) => scene switch
    {
        SceneType.Headshot => "010100",
        SceneType.HalfLength => "010200",
        SceneType.FullLength => "010300",
        SceneType.Profile => "010400",
        SceneType.RearView => "010500",
        SceneType.Single => "010600",
        SceneType.Couple => "010700",
        SceneType.Two => "010800",
        SceneType.Group => "010900",
        SceneType.GeneralView => "011000",
        SceneType.PanoramicView => "011100",
        SceneType.AerialView => "011200",
        SceneType.UnderWater => "011300",
        SceneType.NightScene => "011400",
        SceneType.Satellite => "011500",
        SceneType.ExteriorView => "011600",
        SceneType.InteriorView => "011700",
        SceneType.CloseUp => "011800",
        SceneType.Action => "011900",
        SceneType.Performing => "012000",
        SceneType.Posing => "012100",
        SceneType.Symbolic => "012200",
        SceneType.OffBeat => "012300",
        SceneType.MovieScene => "012400",
        _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, null),
    };
}
