using RaceGame.Cars;

namespace RaceGame.Tuning
{
    /// <summary>Applies a TuningData to a built car: physics and appearance.</summary>
    public static class TuningApplier
    {
        public static void Apply(CarController car, TuningData tuning)
        {
            tuning.Clamp();
            var stats = TuningStats.Compute(car.Spec, tuning);
            car.ApplyTuning(stats);
            if (car.TryGetComponent(out CarVisual visual)) visual.Apply(tuning, stats.wheelRadius);
        }
    }
}
