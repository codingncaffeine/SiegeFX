using SiegeFX.Runtime;
using SiegeFX.Runtime.Render.Hud;

namespace SiegeFX.Tests;

public class MpCharacterPickerTests
{
    static void Spin(MpCharacterPanel panel, int row, bool right = true)
    {
        int x = right ? 712 : 594, y = 136 + 26 * row;
        panel.OnMouseDown(x, y, 800, 600);
        panel.OnMouseUp(x, y, 800, 600);
    }

    [Fact]
    public void HeadWrapsAndRemapsHair()
    {
        var panel = new MpCharacterPanel { HeadCount = 5 };
        panel.OpenCreate(new HeroVariantPicker { HeadIdx = 0, StyleIdx = 2, FaceIdx = 3 });
        panel.HairOnNewHead = (oldPick, newPick) =>
        {
            Assert.Equal(0, oldPick.HeadIdx);
            Assert.Equal(2, oldPick.StyleIdx);
            Assert.Equal(4, newPick.HeadIdx);
            return 1;
        };

        Spin(panel, 1, right: false);

        Assert.Equal(4, panel.Picker.HeadIdx);
        Assert.Equal(1, panel.Picker.StyleIdx);
        Assert.Equal(3, panel.Picker.FaceIdx);
    }

    [Fact]
    public void HairChangesIndependentlyOfHead()
    {
        var panel = new MpCharacterPanel();
        panel.OpenCreate(new HeroVariantPicker { HeadIdx = 4, StyleIdx = 2 });

        Spin(panel, 3);

        Assert.Equal(4, panel.Picker.HeadIdx);
        Assert.Equal(3, panel.Picker.StyleIdx);
    }

    [Fact]
    public void BodyChangeStartsOnItsDefaultLook()
    {
        var panel = new MpCharacterPanel
        {
            DefaultLook = gender => gender == HeroGender.Girl ? (9, 6, 2, 3) : (0, 0, 0, 0),
        };
        panel.OpenCreate(new HeroVariantPicker { HeadIdx = 4, StyleIdx = 2 });

        Spin(panel, 0);

        Assert.Equal(HeroGender.Girl, panel.Picker.Gender);
        Assert.Equal(0, panel.Picker.HeadIdx);
        Assert.Equal(9, panel.Picker.FaceIdx);
        Assert.Equal(6, panel.Picker.StyleIdx);
        Assert.Equal(2, panel.Picker.ShirtIdx);
        Assert.Equal(3, panel.Picker.PantsIdx);
    }

    [Fact]
    public void DwarfAndSkeletonKeepTheirOwnLookAndStillCycle()
    {
        var panel = new MpCharacterPanel();
        panel.OpenCreate(new HeroVariantPicker { Gender = HeroGender.Dwarf });

        Spin(panel, 1);
        Spin(panel, 3);
        Assert.Equal(0, panel.Picker.HeadIdx);
        Assert.Equal(0, panel.Picker.StyleIdx);
        Spin(panel, 0);
        Assert.Equal(HeroGender.Skeleton, panel.Picker.Gender);
        Spin(panel, 0);
        Assert.Equal(HeroGender.Boy, panel.Picker.Gender);
    }
}
