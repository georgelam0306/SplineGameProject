namespace SplineGame.Data;

public sealed class SplineTableIds
{
    public SplineTableIds(
        string playerTableId,
        string enemiesTableId,
        string triggersTableId,
        string gameLevelsTableId)
    {
        PlayerTableId = playerTableId;
        EnemiesTableId = enemiesTableId;
        TriggersTableId = triggersTableId;
        GameLevelsTableId = gameLevelsTableId;
    }

    public string PlayerTableId { get; }

    public string EnemiesTableId { get; }

    public string TriggersTableId { get; }

    public string GameLevelsTableId { get; }
}
