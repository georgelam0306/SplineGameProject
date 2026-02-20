using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;

namespace DerpLib.Rendering;

public sealed class CommandRecorder : IDisposable
{
    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;
    private readonly int _framesInFlight;

    private CommandPool[] _commandPools = [];
    private CommandBuffer[] _commandBuffers = [];
    private int _currentFrame;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    public CommandBuffer CurrentCommandBuffer => _commandBuffers[_currentFrame];

    public CommandRecorder(ILogger log, VkDevice vkDevice, int framesInFlight = 2)
    {
        _log = log;
        _vkDevice = vkDevice;
        _framesInFlight = framesInFlight;
    }

    public unsafe void Initialize()
    {
        _commandPools = new CommandPool[_framesInFlight];
        _commandBuffers = new CommandBuffer[_framesInFlight];

        for (int i = 0; i < _framesInFlight; i++)
        {
            // Create command pool (resettable)
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = _vkDevice.GraphicsQueueFamily,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit
            };

            CommandPool pool;
            var result = Vk.CreateCommandPool(Device, &poolInfo, null, &pool);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create command pool {i}: {result}");
            }
            _commandPools[i] = pool;

            // Allocate command buffer
            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = pool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1
            };

            CommandBuffer cmdBuffer;
            result = Vk.AllocateCommandBuffers(Device, &allocInfo, &cmdBuffer);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to allocate command buffer {i}: {result}");
            }
            _commandBuffers[i] = cmdBuffer;
        }

        _log.Information("CommandRecorder created: {Count} pools/buffers", _framesInFlight);
    }

    public void SetCurrentFrame(int frame)
    {
        _currentFrame = frame;
    }

    public unsafe void BeginRecording()
    {
        var cmdBuffer = _commandBuffers[_currentFrame];

        // Reset the buffer before recording
        Vk.ResetCommandBuffer(cmdBuffer, 0);

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        var result = Vk.BeginCommandBuffer(cmdBuffer, &beginInfo);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to begin command buffer: {result}");
        }
    }

    public CommandBuffer EndRecording()
    {
        var cmdBuffer = _commandBuffers[_currentFrame];

        var result = Vk.EndCommandBuffer(cmdBuffer);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to end command buffer: {result}");
        }

        return cmdBuffer;
    }

    public unsafe void Dispose()
    {
        // Destroying pools automatically frees their command buffers
        foreach (var pool in _commandPools)
        {
            Vk.DestroyCommandPool(Device, pool, null);
        }
    }
}
