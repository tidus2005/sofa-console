namespace MouseSideLab
{
    public static class LogitechChordReport
    {
        public static bool TryRead(byte[] report, int length, byte feature, out bool front, out bool back)
        {
            front = back = false;
            if (report == null || length != 20 || report.Length < length || feature == 0 ||
                report[0] != 0x11 || report[1] != 0xFF || report[2] != feature || report[3] != 0) return false;
            for (int i = 4; i < 12; i += 2)
            {
                int control = report[i] * 256 + report[i + 1];
                if (control == 86) front = true;
                if (control == 83) back = true;
            }
            return true;
        }
    }
}
