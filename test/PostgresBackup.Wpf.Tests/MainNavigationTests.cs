using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

public class MainNavigationTests
{
    [Fact]
    public void Initial_destination_is_settings()
    {
        var navigation = new MainNavigation();

        Assert.Equal(MainPage.Settings, navigation.CurrentPage);
    }

    [Theory]
    [InlineData(MainPage.Settings)]
    [InlineData(MainPage.Backup)]
    [InlineData(MainPage.Restore)]
    [InlineData(MainPage.History)]
    public void User_can_navigate_to_each_available_destination(MainPage destination)
    {
        var navigation = new MainNavigation();

        navigation.NavigateTo(destination);

        Assert.Equal(destination, navigation.CurrentPage);
    }
}
