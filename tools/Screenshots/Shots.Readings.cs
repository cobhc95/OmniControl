using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Resources.Pages;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The Resources page's Readings tab over <see cref="SampleReadings"/>: the real page view-model and cards, no poller, no counters, no
    /// sensor library. The page is told it is on screen only to show the sample's values; its asks for reads go nowhere.
    private static void ResourcesReadings(Rig rig, Camera camera)
    {
        Use(ViewTemplate.For<ResourcesPageViewModel, ResourcesPage>(), ViewTemplate.For<ReadingCard, ReadingRow>());
        var (model, presets, settings) = ResourcesParts(rig);
        var page = new ResourcesPageViewModel(new SampleReadings(), settings, presets, model);
        page.OnOnScreenChanged(true);
        camera.Page(page, "resources-readings");
        page.Dispose();
    }

    /// A PC with two graphics adapters and two disks, every value made up; the Hottest sensor is left out of its list.
    private sealed class SampleReadings : IReadingList
    {
        private static readonly GraphicsAdapter[] Adapters =
        {
            new("pci0-1-0", "Discrete graphics", "0x1"),
            new("pci0-2-0", "Integrated graphics", "0x2"),
        };

        private static readonly PhysicalDisk[] Disks = { new("sample-nvme", 0, "NVMe SSD 1 TB"), new("sample-sata", 1, "SATA SSD 2 TB") };

        public SampleReadings()
        {
            Readings = Reading.All.Where(r => r.Id != Reading.HottestId)
                .Concat(Adapters.SelectMany((g, i) => Reading.OfGpu(g, i + 1))).Concat(Disks.SelectMany(Reading.OfDisk)).ToList();
            Latest = new ResourceSample(12, null, 14.2, 32, 11.9 * 1024 * 1024, 180 * 1024, "Ethernet", null, 48, null, null, null)
            {
                Parts = SampleParts.All,
                Gpus = new[] { new GpuSample(Adapters[0].Id, 37, 61), new GpuSample(Adapters[1].Id, 3, 45) },
                Disks = new[] { new DiskSample(0, 6, 39), new DiskSample(1, 1, 33) },
            };
        }

        public IReadOnlyList<Reading> Readings { get; }
        public IReadOnlyList<GraphicsAdapter> Gpus => Adapters;
        public ResourceSample Latest { get; }
        public string? SensorProblem => null;
        public Reading? Find(string id) => Readings.FirstOrDefault(r => r.Id == id);
        public IReadOnlyList<double> History(string readingId) => Array.Empty<double>();
        public void ListSensors(bool open) { }
        public void Need(string who, TimeSpan? every, SampleParts parts = SampleParts.None) { }
        public event Action? ReadingsChanged { add { } remove { } }
        public event Action? Updated { add { } remove { } }
    }
}
