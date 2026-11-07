namespace Gallery.Web.Components.Molecules;

/// <summary>How many containers hold an item: none, one, or more than one (015 K4).</summary>
public enum Membership { None, One, Many }

public partial class PhotoActions
{
    /// <summary>The membership class of a container count (K4).</summary>
    public static Membership MembershipOf(int count) => count <= 0 ? Membership.None : count == 1 ? Membership.One : Membership.Many;

    /// <summary>The group icon for a membership class (K4).</summary>
    public static string IconOf(Membership m) => m switch
    {
        Membership.None => "folder-plus",
        Membership.One => "folder",
        _ => "folders",
    };

    /// <summary>The group control's accessible name: the FR-006 copy, owner-fixed (K4).</summary>
    public static string CountLabel(int count) => count <= 0 ? "Not in a gallery" : count == 1 ? "In 1 gallery" : $"In {count} galleries";
}
