using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.Services;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

public class ClientToolPresentationTests
{
    [Fact]
    public void New_settings_do_not_claim_client_tools_are_ready()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(ToolDetectionPresentationState.NotChecked, viewModel.ToolPresentationState);
    }

    [Fact]
    public async Task Detection_exposes_detecting_state_until_the_probe_completes()
    {
        var completion = new TaskCompletionSource<ToolDetectionResult>();
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        var viewModel = CreateViewModel(detector: detector.Object);

        var detection = viewModel.DetectToolsAsync();

        Assert.Equal(ToolDetectionPresentationState.Detecting, viewModel.ToolPresentationState);

        completion.SetResult(ReadyResult());
        await detection;
    }

    [Theory]
    [MemberData(nameof(CompletedDetections))]
    public async Task Completed_detection_exposes_its_truthful_presentation_state(
        ToolDetectionResult result,
        ToolDetectionPresentationState expectedState)
    {
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        var viewModel = CreateViewModel(detector: detector.Object);

        await viewModel.DetectToolsAsync();

        Assert.Equal(expectedState, viewModel.ToolPresentationState);
    }

    [Theory]
    [InlineData(ToolDetectionPresentationState.NotChecked, false)]
    [InlineData(ToolDetectionPresentationState.Detecting, false)]
    [InlineData(ToolDetectionPresentationState.Ready, true)]
    [InlineData(ToolDetectionPresentationState.Incompatible, false)]
    [InlineData(ToolDetectionPresentationState.NotFound, false)]
    public void All_tools_ready_copy_is_only_available_for_a_ready_presentation(
        ToolDetectionPresentationState state,
        bool expected)
    {
        var viewModel = CreateViewModel();
        viewModel.ToolPresentationState = state;

        Assert.Equal(expected, viewModel.ShowAllToolsReady);
    }

    [Fact]
    public async Task Successful_custom_directory_is_saved_as_a_user_preference()
    {
        const string customDirectory = @"C:\PostgreSQL\bin";
        var preferences = new FakeClientToolPreferencesStore();
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.DetectAsync(customDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadyResult());
        var viewModel = CreateViewModel(detector.Object, preferences);
        viewModel.CustomPath = customDirectory;

        await viewModel.DetectToolsAsync();

        Assert.Equal(customDirectory, preferences.SavedCustomToolDirectory);
    }

    [Fact]
    public async Task Initialization_loads_the_saved_directory_before_detection()
    {
        const string savedDirectory = @"C:\PostgreSQL\bin";
        var preferences = new FakeClientToolPreferencesStore { LoadedCustomToolDirectory = savedDirectory };
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.DetectAsync(savedDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadyResult());
        var profiles = EmptyProfileRepository();
        var viewModel = new SettingsViewModel(detector.Object, profiles.Object, preferences);

        await viewModel.InitializeAsync();

        Assert.Equal(savedDirectory, viewModel.CustomPath);
    }

    [Fact]
    public async Task Invalid_saved_directory_remains_visible_after_startup_detection()
    {
        const string savedDirectory = @"C:\Former\PostgreSQL\bin";
        var preferences = new FakeClientToolPreferencesStore { LoadedCustomToolDirectory = savedDirectory };
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.DetectAsync(savedDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolDetectionResult.CreateNotFound());
        var viewModel = new SettingsViewModel(detector.Object, EmptyProfileRepository().Object, preferences);

        await viewModel.InitializeAsync();

        Assert.Equal(savedDirectory, viewModel.CustomPath);
    }

    public static TheoryData<ToolDetectionResult, ToolDetectionPresentationState> CompletedDetections =>
        new()
        {
            { ReadyResult(), ToolDetectionPresentationState.Ready },
            { new ToolDetectionResult { Status = ToolStatus.Incompatible }, ToolDetectionPresentationState.Incompatible },
            { ToolDetectionResult.CreateNotFound(), ToolDetectionPresentationState.NotFound },
        };

    private static SettingsViewModel CreateViewModel(
        IToolDetectionService? detector = null,
        IClientToolPreferencesStore? preferences = null) =>
        new(detector ?? new Mock<IToolDetectionService>().Object, EmptyProfileRepository().Object, preferences ?? new FakeClientToolPreferencesStore());

    private static Mock<IConnectionProfileRepository> EmptyProfileRepository()
    {
        var repository = new Mock<IConnectionProfileRepository>();
        repository.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConnectionProfile>());
        return repository;
    }

    private static ToolDetectionResult ReadyResult() => new()
    {
        Status = ToolStatus.Ready,
        Source = DetectionSource.CustomPath
    };

    private sealed class FakeClientToolPreferencesStore : IClientToolPreferencesStore
    {
        public string? LoadedCustomToolDirectory { get; init; }
        public string? SavedCustomToolDirectory { get; private set; }

        public Task<string?> LoadCustomToolDirectoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LoadedCustomToolDirectory);

        public Task SaveCustomToolDirectoryAsync(string customToolDirectory, CancellationToken cancellationToken = default)
        {
            SavedCustomToolDirectory = customToolDirectory;
            return Task.CompletedTask;
        }
    }
}
