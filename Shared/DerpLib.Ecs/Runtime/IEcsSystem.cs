namespace DerpLib.Ecs;

public interface IEcsSystem<in TWorld>
{
    void Update(TWorld world);
}

