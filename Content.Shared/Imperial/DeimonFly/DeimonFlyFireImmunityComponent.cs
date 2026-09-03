using Robust.Shared.GameStates;

namespace Content.Shared.Imperial.DeimonFly;

/// <summary>
/// Даёт сущности полный иммунитет к обычному входящему урону типа Heat.
/// Компонент можно добавить сущности вручную через меню компонентов.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DeimonFlyFireImmunityComponent : Component;
