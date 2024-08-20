using System;

struct ConsoleColors : IDisposable {
    public static ConsoleColors FromForeground(ConsoleColor color) {
        Console.ForegroundColor = color;
        return new ConsoleColors();
    }

    public static ConsoleColors FromBackground(ConsoleColor color) {
        Console.BackgroundColor = color;
        return new ConsoleColors();
    }

    public void Dispose() {
        Console.ResetColor();
    }
}
