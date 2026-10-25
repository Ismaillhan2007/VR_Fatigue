using NUnit.Framework;
using UnityEngine;

namespace VRFatigue.QuestPro.Tests
{
    public sealed class QuestRenderingTests
    {
        [TestCase(false, false, false, false, QuestFoveationMode.Off)]
        [TestCase(true, true, true, false, QuestFoveationMode.Fixed)]
        [TestCase(true, true, false, true, QuestFoveationMode.Fixed)]
        [TestCase(true, false, true, true, QuestFoveationMode.Fixed)]
        [TestCase(true, true, true, true, QuestFoveationMode.EyeTracked)]
        public void ModeRequiresCapabilityConsentAndPermission(bool fixedSupport, bool eyeSupport,
            bool permission, bool consent, QuestFoveationMode expected)
        {
            var policy = new QuestRenderingPolicy(); policy.SetConsent(consent);
            Assert.That(policy.Choose(fixedSupport, eyeSupport, permission), Is.EqualTo(expected));
        }

        [Test]
        public void GrantDuringAssessmentDoesNotChangeBaselineConditions()
        {
            var policy = new QuestRenderingPolicy();
            Assert.That(policy.BeginAssessment(QuestFoveationMode.Fixed, 90), Is.True);
            policy.SetConsent(true);
            Assert.That(policy.Choose(true, true, true), Is.EqualTo(QuestFoveationMode.Fixed));
            policy.EndAssessment();
            Assert.That(policy.Choose(true, true, true), Is.EqualTo(QuestFoveationMode.EyeTracked));
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void WithdrawalOrRevocationLeavesEtfrAndInvalidatesAssessment(bool consent, bool permission)
        {
            var policy = new QuestRenderingPolicy(); policy.SetConsent(true);
            policy.BeginAssessment(QuestFoveationMode.EyeTracked, 90);
            policy.SetConsent(consent);
            var next = policy.Choose(true, true, permission);
            Assert.That(next, Is.EqualTo(QuestFoveationMode.Fixed));
            Assert.That(policy.ConditionsMatch(next, 90), Is.False);
        }

        [Test]
        public void DisplayRateChangeInvalidatesAssessmentAndUnsupportedRatesAreNotRequested()
        {
            var policy = new QuestRenderingPolicy(); policy.BeginAssessment(QuestFoveationMode.Fixed, 90);
            Assert.That(policy.ConditionsMatch(QuestFoveationMode.Fixed, 72), Is.False);
            Assert.That(QuestRenderingPolicy.Supports90Hz(new[] { 72f, 80f, float.NaN }), Is.False);
            Assert.That(QuestRenderingPolicy.Supports90Hz(new[] { 72f, 90f }), Is.True);
            Assert.That(QuestRenderingPolicy.Supports90Hz(null), Is.False);
        }

        [TestCase(0f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void UnavailableDisplayCannotStartAssessment(float hz)
        {
            Assert.That(new QuestRenderingPolicy().BeginAssessment(QuestFoveationMode.Fixed, hz), Is.False);
        }

        [Test]
        public void WaitForAsynchronous90HzReadbackBeforeStartingAssessment()
        {
            Assert.That(QuestRenderingPolicy.CadenceReady(new[] { 72f, 90f }, 72), Is.False);
            Assert.That(QuestRenderingPolicy.CadenceReady(new[] { 72f, 90f }, 90), Is.True);
            Assert.That(QuestRenderingPolicy.CadenceReady(new[] { 72f }, 72), Is.True);
            Assert.That(QuestRenderingPolicy.CadenceReady(null, 90), Is.False);
            Assert.That(QuestRenderingPolicy.CadenceReady(new[] { 90f }, 0), Is.False);
        }

        [Test]
        public void TrackingCapabilityLossInvalidatesLockedEyeMode()
        {
            var policy = new QuestRenderingPolicy(); policy.SetConsent(true);
            policy.BeginAssessment(QuestFoveationMode.EyeTracked, 90);
            Assert.That(policy.ConditionsMatch(policy.Choose(true, false, true), 90), Is.False);
            Assert.That(policy.ConditionsMatch(policy.Choose(false, false, true), 90), Is.False);
        }

        [Test]
        public void AssessmentCannotBeLockedTwiceOrWithUnknownMode()
        {
            var policy = new QuestRenderingPolicy();
            Assert.That(policy.BeginAssessment(QuestFoveationMode.Unavailable, 90), Is.False);
            Assert.That(policy.BeginAssessment(QuestFoveationMode.Fixed, 90), Is.True);
            Assert.That(policy.BeginAssessment(QuestFoveationMode.EyeTracked, 90), Is.False);
        }

        [Test]
        public void HeadCompensationPreservesEyeOffsetAndRemovesHeadYaw()
        {
            var head = Quaternion.Euler(0, 65, 0);
            var local = Quaternion.Euler(0, -12, 0);
            Assert.That(QuestGazeGeometry.TryHeadLocalDirection(head * local, head, out var result), Is.True);
            Assert.That(Vector3.Angle(result, local * Vector3.forward), Is.LessThan(0.01f));
        }

        [Test]
        public void InvalidRotationCannotBecomeForwardGaze()
        {
            Assert.That(QuestGazeGeometry.TryHeadLocalDirection(Quaternion.identity, default, out _), Is.False);
            Assert.That(QuestGazeGeometry.TryHeadLocalDirection(new Quaternion(float.NaN, 0, 0, 1),
                Quaternion.identity, out _), Is.False);
        }

        [Test]
        public void ScaledQuaternionsHaveTheSameGazeDirection()
        {
            var head = Quaternion.Euler(12, -25, 8);
            var eye = head * Quaternion.Euler(4, 10, 0);
            var scaledHead = new Quaternion(head.x * 2, head.y * 2, head.z * 2, head.w * 2);
            Assert.That(QuestGazeGeometry.TryHeadLocalDirection(eye, scaledHead, out var result), Is.True);
            Assert.That(Vector3.Angle(result, Quaternion.Euler(4, 10, 0) * Vector3.forward), Is.LessThan(0.01f));
        }

        [Test]
        public void DesktopDoesNotInstallOrPromptForQuestRendering()
        {
            Assert.That(QuestProRenderingController.EnsureInstalled(), Is.Null);
        }
    }
}
