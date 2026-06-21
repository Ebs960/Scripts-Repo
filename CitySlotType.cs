/// <summary>
/// Strategic building slot categories available in a city.
/// Buildings can declare one or more allowed slot types and cities track capacity by type.
/// </summary>
public enum CitySlotType
{
    Housing,
    Food,
    Production,
    Commerce,
    Science,
    Culture,
    Faith,
    Military,
    Defense,
    Infrastructure,
    Specialist,
    Wonder,
    Utility
}
