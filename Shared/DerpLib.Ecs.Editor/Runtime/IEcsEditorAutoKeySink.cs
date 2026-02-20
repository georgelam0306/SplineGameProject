namespace DerpLib.Ecs.Editor;

public interface IEcsEditorAutoKeySink
{
    void OnCommand(in EcsEditorCommand command);
}

