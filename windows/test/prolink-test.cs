// Runtime test for the ProLink listener: runs the REAL class from MatrixBG.cs under
// mono (it is pure .NET sockets, no Win32), fires synthetic Pro DJ Link beat packets
// at 127.0.0.1:50001, and checks the receive -> validate -> parse -> consume path.
//
// Build + run (from windows/):
//   mcs -sdk:4.5 -unsafe -main:MatrixBG.ProLinkTest -r:System.dll -r:System.Drawing.dll \
//       -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll \
//       -out:test/prolink-test.exe MatrixBG.cs test/prolink-test.cs && mono test/prolink-test.exe
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MatrixBG
{
    static class ProLinkTest
    {
        static int failures = 0;
        static void Check(bool ok, string what)
        {
            if (!ok) { failures++; Console.WriteLine("FAIL: " + what); }
            else Console.WriteLine("ok:   " + what);
        }

        // A minimal 96-byte beat packet per the Deep Symmetry DJ Link analysis.
        static byte[] Packet(byte type, int device, long pitch, int bpm100, byte bar)
        {
            byte[] p = new byte[0x60];
            byte[] magic = { 0x51, 0x73, 0x70, 0x74, 0x31, 0x57, 0x6D, 0x4A, 0x4F, 0x4C };
            Array.Copy(magic, p, magic.Length);
            p[0x0A] = type;
            p[0x21] = (byte)device;
            p[0x54] = (byte)(pitch >> 24); p[0x55] = (byte)(pitch >> 16); p[0x56] = (byte)(pitch >> 8); p[0x57] = (byte)pitch;
            p[0x5A] = (byte)(bpm100 >> 8); p[0x5B] = (byte)bpm100;
            p[0x5C] = bar;
            return p;
        }

        static bool WaitBeat(ProLink link, out int bar, int timeoutMs)
        {
            bar = 0;
            for (int i = 0; i < timeoutMs / 10; i++)
            {
                if (link.TakeBeat(out bar)) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        static int Main()
        {
            ProLink link = new ProLink();
            if (!link.Open()) { Console.WriteLine("FATAL: could not bind udp 50001: " + link.LastError); return 2; }
            Check(link.Running, "listener bound on udp 50001");

            UdpClient send = new UdpClient();
            IPEndPoint dst = new IPEndPoint(IPAddress.Loopback, 50001);
            Action<byte[]> fire = delegate(byte[] p) { send.Send(p, p.Length, dst); };
            int bar;

            // 1. A valid beat: CDJ #2, neutral pitch, 128.00 BPM, downbeat
            fire(Packet(0x28, 2, 0x100000, 12800, 1));
            Check(WaitBeat(link, out bar, 2000), "beat received");
            Check(bar == 1, "beat-within-bar == 1 (got " + bar + ")");
            Check(link.BpmTimes100 == 12800, "BPM 128.00 at neutral pitch (got " + link.BpmTimes100 + ")");
            Check(link.LastDevice == 2, "device number 2 (got " + link.LastDevice + ")");
            Check(link.Hearing, "Hearing reports true after a beat");

            // 2. Pitch math: +6.25% (0x110000) on 128 BPM = 136.00
            fire(Packet(0x28, 3, 0x110000, 12800, 2));
            Check(WaitBeat(link, out bar, 2000), "pitched beat received");
            Check(bar == 2, "bar 2 travels with its beat (got " + bar + ")");
            Check(link.BpmTimes100 == 13600, "pitch-adjusted BPM 136.00 (got " + link.BpmTimes100 + ")");

            // 3. Garbage pitch falls back to neutral
            fire(Packet(0x28, 2, 0xFFFFFF, 12000, 3));
            Check(WaitBeat(link, out bar, 2000), "garbage-pitch beat still accepted");
            Check(link.BpmTimes100 == 12000, "garbage pitch treated as neutral (got " + link.BpmTimes100 + ")");

            // 4. Rejections: BPM sentinel, wrong type, wrong magic, short packet, insane BPM.
            // Rejected packets must not just skip the beat counter: they must leave ALL
            // published state untouched (BpmSync reads BpmTimes100/LastDevice outside TakeBeat).
            int bpmBefore = link.BpmTimes100, devBefore = link.LastDevice, barBefore = link.LastBar;
            fire(Packet(0x28, 7, 0x100000, 0xFFFF, 4));         // empty-deck sentinel
            fire(Packet(0x29, 7, 0x100000, 11111, 4));          // not a beat packet
            byte[] badMagic = Packet(0x28, 7, 0x100000, 11111, 4); badMagic[0] = 0x00; fire(badMagic);
            byte[] shortPkt = new byte[0x30]; fire(shortPkt);   // too short
            fire(Packet(0x28, 7, 0x100000, 40000, 4));          // 400 BPM: outside sanity band
            Check(!WaitBeat(link, out bar, 700), "sentinel/invalid packets all rejected");
            Check(link.BpmTimes100 == bpmBefore && link.LastDevice == devBefore && link.LastBar == barBefore,
                  "rejected packets left published state untouched");

            // 5. Out-of-range bar byte reports 0 (unknown), beat still counts
            fire(Packet(0x28, 4, 0x100000, 12800, 9));
            Check(WaitBeat(link, out bar, 2000), "beat with weird bar byte received");
            Check(bar == 0, "bar out of 1..4 reported as 0 (got " + bar + ")");

            // 6. Close/reopen: no error carryover, listener works again
            link.Close();
            Check(!link.Running, "closed");
            Check(link.Open(), "reopened");
            Check(link.LastError.Length == 0, "no stale error after reopen (got '" + link.LastError + "')");
            fire(Packet(0x28, 2, 0x100000, 9000, 1));
            Check(WaitBeat(link, out bar, 2000), "beats flow after reopen");
            Check(link.BpmTimes100 == 9000, "BPM 90.00 after reopen (got " + link.BpmTimes100 + ")");

            link.Dispose();
            send.Close();
            Console.WriteLine(failures == 0 ? "\nPASS: ProLink runtime test" : "\n" + failures + " FAILURES");
            return failures == 0 ? 0 : 1;
        }
    }
}
