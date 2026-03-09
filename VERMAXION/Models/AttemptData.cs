using System;

namespace VERMAXION.Models;

[Serializable]
public class AttemptData
{
    public int VerminionAttempts { get; set; } = 0;
    public int MiniCactpotAttempts { get; set; } = 0;
    public int ChocoboRacesCompleted { get; set; } = 0;
    public int FCBuffAttempts { get; set; } = 0;

    public void ResetDaily()
    {
        MiniCactpotAttempts = 0;
        ChocoboRacesCompleted = 0;
        FCBuffAttempts = 0;
    }

    public void ResetWeekly()
    {
        VerminionAttempts = 0;
        ResetDaily();
    }
}
