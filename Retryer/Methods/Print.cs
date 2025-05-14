using System;

namespace Retryer.Methods
{
    internal class Print
    {
        private static bool NeedsColor()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEEDS_COLOR")))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        // 正常输出直接
        // Console.WriteLine("");

        public static void PrintInfo(string message)
        {
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Gray;
            }
            Console.WriteLine($"[INFO] {message}");
            if (NeedsColor())
            {
                Console.ResetColor();
            }
        }

        public static void PrintWarning(string message)
        {
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            Console.WriteLine($"[WARNING] {message}");
            if (NeedsColor())
            {
                Console.ResetColor();
            }
        }

        public static void PrintError(string message)
        {
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            Environment.ExitCode = 1;
            Console.WriteLine($"[ERROR] {message}");
            if (NeedsColor())
            {
                Console.ResetColor();
            }
        }

        public static void PrintDebug(string message)
        {
#if DEBUG
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
            }
            Console.WriteLine($"[Debug] {message}");
            if (NeedsColor())
            {
                Console.ResetColor();
            }
#endif
        }

        public static void PrintHint(string message)
        {
            if (NeedsColor())
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
            }
            Console.WriteLine($"[Hint] {message}");
            if (NeedsColor())
            {
                Console.ResetColor();
            }
        }
    }
}
