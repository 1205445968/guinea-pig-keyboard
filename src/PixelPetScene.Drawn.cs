using System;
using System.Windows;

namespace WheekKeys
{
    internal sealed partial class PixelPetScene
    {
        private struct DrawnPose
        {
            public Rect Body;
            public Point LeftTip, RightTip;
            public Point RearLeftTip, RearRightTip;
        }

        private DrawnPose CurrentDrawnPose(double now)
        {
            if (drawnProne.Compact) return CurrentCompactPose(now);
            Point lp = left.Position(now), rp = right.Position(now);
            double grip = Smooth(mouseGrip);
            double lean = Math.Max(-78, Math.Min(115, ((lp.X - left.Rest.X) + (rp.X - right.Rest.X)) * 0.84));
            lean += (mousePosition.X - 8 - 135 - lean) * grip;
            double reachY = Math.Max(0, Math.Max(lp.Y - left.Rest.Y, rp.Y - right.Rest.Y));
            double keyDip = drawnProne.Compact ? Math.Min(55, reachY * 0.95) : Math.Min(19, reachY * 0.42);
            double dip = keyDip * (1 - grip) + ((drawnProne.Compact ? 30 : 12) + (mousePosition.Y - 235) * 0.45) * grip;
            lp += new Vector(lean, dip * 0.68) * left.RestWeight(now);
            rp += new Vector(lean, dip * 0.68) * right.RestWeight(now);
            Point brace = mousePosition + new Vector(79, -7);
            rp += (brace - rp) * (grip * right.RestWeight(now));
            double press = Contact(now - lastClick);
            Point palm = mousePosition + new Vector(3, -2);
            Point mouseTip = palm + (MouseButtonPoint() - palm) * press;
            double scroll = Math.Max(0, 1 - (now - lastScroll) / ScrollDuration);
            mouseTip.Y -= 11 * scrollDirection * scroll;
            lp += (mouseTip - lp) * grip;
            lp.Y -= Math.Sin(grip * Math.PI) * 5;
            Rect body = new Rect(135 + lean, 208 - drawnProne.BodyHeight + dip, 129, drawnProne.BodyHeight);
            if (drawnProne.Compact)
            {
                lp += (drawnProne.RestTip(body, false) - lp) * (left.RestWeight(now) * (1 - grip));
                rp += (drawnProne.RestTip(body, true) - rp) * right.RestWeight(now);
            }
            return new DrawnPose { Body = body, LeftTip = lp, RightTip = rp };
        }

        private void AdvanceDrawnMouse(double now, double elapsed, double readiness)
        {
            bool holding = now - lastMouseUse < 520 || settlingMouse || pendingMouseClick;
            bool typing = double.IsNegativeInfinity(lastMouseUse);
            double release = Math.Max(0, Math.Min(1, (slowMouseUntil - now) / 600));
            mouseGrip = holding ? Math.Min(1, mouseGrip + elapsed * readiness / (145 + (1 - readiness) * 650))
                : Math.Max(0, mouseGrip - elapsed * readiness / ((typing ? 55 : 195) + (1 - readiness) * 220 + release * 220));
            double traction = Smooth(Math.Max(0, (mouseGrip - 0.64) / 0.36));
            mousePosition += (mouseDestination - mousePosition) * (1 - Math.Exp(-elapsed * readiness / (48 + (1 - readiness) * 170))) * traction;
            if ((mouseDestination - mousePosition).LengthSquared < 0.01) mousePosition = mouseDestination;
            if (!holding && mouseGrip == 0) mouseDestination = mousePosition;
            if (mouseGrip >= 0.9 && (settlingMouse || pendingMouseClick))
            {
                if (pendingMouseClick) lastClick = now;
                settlingMouse = pendingMouseClick = false;
                lastMouseUse = now;
            }
        }
    }
}
