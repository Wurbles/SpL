using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KB.Shadekin;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true)]
public sealed partial class ShadekinVisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active = true;
}