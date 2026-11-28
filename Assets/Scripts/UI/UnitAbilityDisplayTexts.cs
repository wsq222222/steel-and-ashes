using UnityEngine;

/// <summary>Название и описание способностей для UI (расстановка, тактика).</summary>
public static class UnitAbilityDisplayTexts
{
    public static string GetAbilityTitle(ChessUnitType type)
    {
        switch (type)
        {
            case ChessUnitType.Horse: return "JUMP";
            case ChessUnitType.Cyclops: return "DEFLECT";
            case ChessUnitType.Hound: return "SHIELD";
            case ChessUnitType.Queen: return "BOOST";
            case ChessUnitType.King: return "HEAL";
            case ChessUnitType.Hammer: return "—";
            default: return "ABILITY";
        }
    }

    public static string GetAbilityDescription(ChessUnitType type)
    {
        switch (type)
        {
            case ChessUnitType.Hammer:
                return "В этом режиме у Молота нет активной способности.";
            case ChessUnitType.Horse:
                return "Телепортация на ближайшую валидную клетку в направлении взгляда.";
            case ChessUnitType.Cyclops:
                return "Отражает входящий урон обратно атакующему на следующий ход врага.";
            case ChessUnitType.Hound:
                return "Снижает входящий урон на 50% на следующий ход врага.";
            case ChessUnitType.Queen:
                return "Увеличивает урон на 20% в текущем ходу.";
            case ChessUnitType.King:
                return "Восстанавливает 20% HP выбранному союзному юниту.";
            default:
                return "";
        }
    }

    public static string GetAbilityCooldownLine(Unit unit)
    {
        if (unit == null) return "";

        UnitAbilities abilities = unit.GetComponent<UnitAbilities>();
        if (abilities == null)
            return "COOLDOWN —";

        int cooldown = abilities.GetAbilityCooldown();
        if (cooldown > 0)
            return $"COOLDOWN {cooldown} TURN(S)";

        return "COOLDOWN READY";
    }
}
