namespace DerpLib.AssetPipeline;

public interface IObjectDatabase
{
    ObjectId Put(byte[] data);
    byte[] Get(ObjectId id);
    bool Has(ObjectId id);
}
