using System.Collections.Generic;
using Godot;
using ProjectXs.Props;

namespace ProjectXs.Vehicles;

/// <summary>Koltuktaki sürücünün son gönderdiği girdi.</summary>
public struct DriveInput
{
    public float Throttle; // -1 geri, +1 ileri
    public float Steer;    // -1 sol, +1 sağ
    public bool Boost;
    public ulong ReceivedAtMs;
}

/// <summary>
/// Sunucuda, koltuğa bağlı aracı sürer.
///
/// Araç "kavramı" yok: koltuk, kaynak ve menteşelerle ona bağlı olan her şey bir araçtır.
/// Hangi tekerleğin solda, hangisinin sağda olduğu koltuğa göre her karede hesaplanır;
/// bu yüzden tekerlekleri istediğin yere takabilirsin.
///
/// Dönüş üç parçadan oluşur:
/// 1. Tank dönüşü: sağ ve sol tekerlekler farklı hızda döner.
/// 2. Tutuş azaltma: dönerken tekerleklerin sürtünmesi düşer, böylece yanlara sürtünen tekerlekler aracı kilitlemez.
/// 3. Dönüş desteği: araca doğrudan bir dönme ivmesi verilir (arcade his için).
/// </summary>
public static class VehicleDriver
{
    private const float YawAssistGain = 8.0f;
    private const float BodyInertiaFactor = 0.3f; // Her parçanın kendi merkezi etrafındaki dönme direnci için kaba tahmin

    public static void Drive(Prop seat, List<Prop> wheels, List<Prop> bodies, DriveInput input, VehicleSettings s)
    {
        bool steering = Mathf.Abs(input.Steer) > 0.01f;
        bool braking = Mathf.Abs(input.Throttle) < 0.01f && !steering;
        float boost = input.Boost ? s.BoostMultiplier : 1.0f;

        DriveWheels(seat, wheels, input, s, steering, braking, boost);

        if (steering && s.YawAssistRate > 0 && s.YawAssistStrength > 0)
            ApplyYawAssist(seat, bodies, input.Steer, s);
    }

    private static void DriveWheels(Prop seat, List<Prop> wheels, DriveInput input, VehicleSettings s,
        bool steering, bool braking, float boost)
    {
        Vector3 right = seat.GlobalBasis.X.Normalized();

        foreach (var wheel in wheels)
        {
            if (!PropCatalog.TryGet(wheel.TypeId, out var definition)) continue;

            // Dönerken tutuşu düşür, düz giderken geri getir
            SetGrip(wheel, definition.Friction * (steering ? s.SteeringGrip : 1.0f));

            float radius = Mathf.Max(definition.Size.X * 0.5f, 0.05f);
            Vector3 axis = wheel.GlobalBasis.Y.Normalized(); // Silindirin dönme ekseni kendi Y ekseni

            // Ekseni koltuğun sağ-sol yönünde olmayan tekerlekleri sürmüyoruz (ör. yatay duran bir tekerlek)
            float side = axis.Dot(right);
            if (Mathf.Abs(side) < 0.3f) continue;

            // Koltuğun solundaki tekerlekler sağa dönerken hızlanır, sağdakiler yavaşlar
            float lateral = (wheel.GlobalPosition - seat.GlobalPosition).Dot(right);
            float steerSign = lateral < 0 ? 1.0f : -1.0f;
            float speed = (input.Throttle * s.MaxSpeed + steerSign * input.Steer * s.TurnSpeed) * boost;

            // İleri yuvarlanmak için açısal hız vektörü koltuğun sol yönünü göstermeli: ω = -sağ * v / r
            float targetSpin = -speed / radius * Mathf.Sign(side);
            float currentSpin = wheel.AngularVelocity.Dot(axis);

            float maxTorque = (braking ? s.BrakeTorque : s.MotorTorque) * boost;
            float torque = Mathf.Clamp((targetSpin - currentSpin) * s.MotorGain, -maxTorque, maxTorque);
            if (Mathf.Abs(torque) < 0.01f) continue;

            wheel.Sleeping = false;
            wheel.ApplyTorque(axis * torque);
        }
    }

    // Bütün aracı ortak kütle merkezi etrafında döndürmeye çalışır. Her parçaya hem dönme yönünde bir itme
    // hem de kendi etrafında küçük bir tork veriyoruz; böylece kaynaklar zorlanmadan araç tek parça gibi döner.
    private static void ApplyYawAssist(Prop seat, List<Prop> bodies, float steer, VehicleSettings s)
    {
        if (bodies.Count == 0) return;

        Vector3 up = seat.GlobalBasis.Y.Normalized();

        float totalMass = 0;
        Vector3 center = Vector3.Zero;
        foreach (var body in bodies)
        {
            totalMass += body.Mass;
            center += body.GlobalPosition * body.Mass;
        }
        if (totalMass <= 0) return;
        center /= totalMass;

        // Sağa dönmek (D) yukarı eksen etrafında saat yönünde, yani negatif dönüş demek
        float targetYawRate = -steer * s.YawAssistRate;
        float currentYawRate = seat.AngularVelocity.Dot(up);
        float acceleration = Mathf.Clamp((targetYawRate - currentYawRate) * YawAssistGain,
            -s.YawAssistStrength, s.YawAssistStrength);
        Vector3 angularAcceleration = up * acceleration;

        foreach (var body in bodies)
        {
            if (body.Freeze) continue;

            Vector3 offset = body.GlobalPosition - center;
            offset -= up * offset.Dot(up);

            body.Sleeping = false;
            body.ApplyCentralForce(body.Mass * angularAcceleration.Cross(offset));
            body.ApplyTorque(angularAcceleration * body.Mass * BodyInertiaFactor);
        }
    }

    private static void SetGrip(Prop wheel, float friction)
    {
        var material = wheel.PhysicsMaterialOverride;
        if (material == null || Mathf.IsEqualApprox(material.Friction, friction)) return;
        material.Friction = friction;
    }
}
