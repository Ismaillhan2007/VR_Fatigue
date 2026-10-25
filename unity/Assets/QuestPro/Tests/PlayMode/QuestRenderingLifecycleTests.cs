using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRFatigue.QuestPro.Tests
{
    public sealed class QuestRenderingLifecycleTests
    {
        [UnityTest]
        public IEnumerator DesktopRenderingDoesNotChangeCadenceOrClaimEtfr()
        {
            var cadence = Application.targetFrameRate;
            var vsync = QualitySettings.vSyncCount;
            var owner = new GameObject("Rendering lifecycle test");
            try
            {
                var renderer = owner.AddComponent<QuestProRenderingController>();
                renderer.SetParticipantConsent(true);
                yield return null;
                Assert.That(renderer.ActualMode, Is.EqualTo(QuestFoveationMode.Unavailable));
                Assert.That(renderer.ActualDisplayHz, Is.Zero);
                Assert.That(renderer.TryBeginAssessment(), Is.False);
                Assert.That(Application.targetFrameRate, Is.EqualTo(cadence));
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(vsync));
                renderer.SetParticipantConsent(false);
                owner.SetActive(false);
                renderer.SetParticipantConsent(true);
                Assert.That(renderer.TryBeginAssessment(), Is.False);
                Assert.That(renderer.Status, Is.EqualTo("controller_disabled"));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [UnityTest]
        public IEnumerator UnsupportedCaptureCannotStartOrRequestPermissionOnDesktop()
        {
            var owner = new GameObject("Capture lifecycle test");
            try
            {
                var capture = owner.AddComponent<QuestProCaptureController>();
                Assert.That(capture.RequestPermission(false), Is.False);
                Assert.That(capture.RequestPermission(true), Is.False);
                Assert.That(capture.BeginCapture(false, true), Is.False);
                Assert.That(capture.Provider.StatusReason, Is.EqualTo("consent_required"));
                Assert.That(capture.BeginCapture(true, true), Is.False);
                Assert.That(capture.Provider.StatusReason, Is.EqualTo("hardware_unsupported"));
                yield return null;
                Assert.That(capture.TryFinishCapture(out var session), Is.False);
                Assert.That(session, Is.Null);
                capture.WithdrawConsent();
                capture.ResetParticipant();
                Assert.That(capture.Provider.State, Is.EqualTo("idle"));
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
