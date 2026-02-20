using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace DerpLib.Rendering;

public sealed class SyncManager : IDisposable
{
    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;
    private readonly int _framesInFlight;

    private Fence[] _inFlightFences = [];
    private VkSemaphore[] _imageAvailableSemaphores = [];
    private VkSemaphore[] _renderFinishedSemaphores = [];
    private int _currentFrame;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    public int FramesInFlight => _framesInFlight;
    public int CurrentFrame => _currentFrame;

    public Fence CurrentFence => _inFlightFences[_currentFrame];
    public VkSemaphore CurrentImageAvailableSemaphore => _imageAvailableSemaphores[_currentFrame];
    public VkSemaphore CurrentRenderFinishedSemaphore => _renderFinishedSemaphores[_currentFrame];

    public SyncManager(ILogger log, VkDevice vkDevice, int framesInFlight = 2)
    {
        _log = log;
        _vkDevice = vkDevice;
        _framesInFlight = framesInFlight;
    }

    public unsafe void Initialize()
    {
        _inFlightFences = new Fence[_framesInFlight];
        _imageAvailableSemaphores = new VkSemaphore[_framesInFlight];
        _renderFinishedSemaphores = new VkSemaphore[_framesInFlight];

        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit // Start signaled so first frame doesn't block
        };

        var semaphoreInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        for (int i = 0; i < _framesInFlight; i++)
        {
            Fence fence;
            var result = Vk.CreateFence(Device, &fenceInfo, null, &fence);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create fence {i}: {result}");
            }
            _inFlightFences[i] = fence;

            VkSemaphore imageAvailable;
            result = Vk.CreateSemaphore(Device, &semaphoreInfo, null, &imageAvailable);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create imageAvailable semaphore {i}: {result}");
            }
            _imageAvailableSemaphores[i] = imageAvailable;

            VkSemaphore renderFinished;
            result = Vk.CreateSemaphore(Device, &semaphoreInfo, null, &renderFinished);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create renderFinished semaphore {i}: {result}");
            }
            _renderFinishedSemaphores[i] = renderFinished;
        }

        _log.Information("SyncManager created: {Count} frames in flight", _framesInFlight);
    }

    public unsafe void WaitForCurrentFence()
    {
        var fence = _inFlightFences[_currentFrame];
        Vk.WaitForFences(Device, 1, &fence, true, ulong.MaxValue);
    }

    public unsafe void ResetCurrentFence()
    {
        var fence = _inFlightFences[_currentFrame];
        Vk.ResetFences(Device, 1, &fence);
    }

    public void AdvanceFrame()
    {
        _currentFrame = (_currentFrame + 1) % _framesInFlight;
    }

    public unsafe void Dispose()
    {
        foreach (var fence in _inFlightFences)
        {
            Vk.DestroyFence(Device, fence, null);
        }
        foreach (var semaphore in _imageAvailableSemaphores)
        {
            Vk.DestroySemaphore(Device, semaphore, null);
        }
        foreach (var semaphore in _renderFinishedSemaphores)
        {
            Vk.DestroySemaphore(Device, semaphore, null);
        }
    }
}
