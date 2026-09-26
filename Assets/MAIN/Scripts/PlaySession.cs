using UnityEngine.SceneManagement;

/// <summary>
/// Какой режим выбран. Меню — сцена game, лодка — main, дом — house.
/// </summary>
public static class PlaySession
{
    public const string MenuScene = "game";
    public const string BoatScene = "main";
    public const string HouseScene = "house";

    public enum Mode
    {
        None,
        BoatRace,
        HouseHold
    }

    public static Mode Active { get; private set; } = Mode.None;

    public static bool MenuOpen => Active == Mode.None;

    public static bool IsMenuScene => SceneName == MenuScene;
    public static bool IsBoatScene => SceneName == BoatScene;
    public static bool IsHouseScene => SceneName == HouseScene;

    static string SceneName => SceneManager.GetActiveScene().name;

    public static bool HandsLocked
    {
        get
        {
            if (MenuOpen)
                return true;
            if (BoatRaceMode.Current != null && BoatRaceMode.Current.HandsLocked)
                return true;
            if (HouseBuildMode.Current != null && HouseBuildMode.Current.HandsLocked)
                return true;
            return false;
        }
    }

    public static void Choose(Mode mode)
    {
        Active = mode;
    }

    public static void ResetPick()
    {
        Active = Mode.None;
    }
}
