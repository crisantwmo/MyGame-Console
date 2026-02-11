using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

class Program
{
    static void Main()
    {
        // Captura de excepciones no controladas (por si un hilo explota)
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Console.Clear();
            Console.WriteLine("ERROR NO CONTROLADO:");
            Console.WriteLine(e.ExceptionObject?.ToString());
            Console.WriteLine("\nPresiona cualquier tecla para salir...");
            Console.ReadKey(true);
        };

        new Game().Run();
    }
}

// ----------------- INTERFACES -----------------
interface IDamageable
{
    void TakeDamage(int damage);
    bool IsAlive { get; }
}

// ----------------- ENTIDADES -----------------
abstract class Entity
{
    public int X { get; protected set; }
    public int Y { get; protected set; }

    protected Entity(int x, int y)
    {
        X = x;
        Y = y;
    }

    public virtual void Move(int dx, int dy) { X += dx; Y += dy; }
    public abstract void DrawInto(char[,] frame);
}

class Player : Entity, IDamageable
{
    public int HP { get; private set; } = 100;
    public int lastDx = 0, lastDy = -1;
    public Dictionary<string, int> Stats = new Dictionary<string, int> {
        ["kills"] = 0, ["shotsFired"] = 0
    };

    public Player(int x, int y) : base(x, y) { }

    public void MoveTo(int newX, int newY) { X = newX; Y = newY; }
    public void TakeDamage(int dmg) { HP -= dmg; }
    public bool IsAlive => HP > 0;

    public override void DrawInto(char[,] frame)
    {
        if (Inside(frame, X, Y)) frame[Y, X] = '@';
    }

    protected static bool Inside(char[,] a, int x, int y) =>
        y >= 0 && y < a.GetLength(0) && x >= 0 && x < a.GetLength(1);
}

abstract class Monster : Entity, IDamageable
{
    public int HP { get; private set; }
    protected Monster(int x, int y, int hp) : base(x, y) { HP = hp; }

    public void TakeDamage(int dmg) { HP -= dmg; }
    public bool IsAlive => HP > 0;

    public override void DrawInto(char[,] frame)
    {
        if (IsAlive && Inside(frame, X, Y)) frame[Y, X] = 'M';
    }

    public abstract void Update(Player player, char[,] layout);

    protected static bool Inside(char[,] a, int x, int y) =>
        y >= 0 && y < a.GetLength(0) && x >= 0 && x < a.GetLength(1);
}

class ChaserMonster : Monster
{
    public ChaserMonster(int x, int y) : base(x, y, 50) { }

    public override void Update(Player player, char[,] layout)
    {
        if (!IsAlive) return;

        int bestX = X, bestY = Y;
        int minDist = Math.Abs(player.X - X) + Math.Abs(player.Y - Y);
        int[,] dirs = { {0,-1},{0,1},{-1,0},{1,0} };

        for (int i = 0; i < 4; i++)
        {
            int nx = X + dirs[i, 0], ny = Y + dirs[i, 1];
            if (ny >= 0 && ny < layout.GetLength(0) &&
                nx >= 0 && nx < layout.GetLength(1) &&
                layout[ny, nx] != '#')
            {
                int d = Math.Abs(player.X - nx) + Math.Abs(player.Y - ny);
                if (d < minDist) { minDist = d; bestX = nx; bestY = ny; }
            }
        }
        X = bestX; Y = bestY;
    }
}

class PatrolMonster : Monster
{
    private int direction = 1;
    private readonly int minX, maxX;

    public PatrolMonster(int x, int y, int minX, int maxX) : base(x, y, 50)
    { this.minX = minX; this.maxX = maxX; }

    public override void Update(Player player, char[,] layout)
    {
        if (!IsAlive) return;
        int nx = X + direction;

        // Si nos salimos del rango, invertimos; gracias al OR corto, no accede a layout si está fuera
        if (nx < minX || nx > maxX || layout[Y, nx] == '#')
            direction *= -1;

        X += direction;
    }

    public override void DrawInto(char[,] frame)
    {
        if (IsAlive && Inside(frame, X, Y)) frame[Y, X] = 'P';
    }
}

// ----------------- OTROS OBJETOS -----------------
class Bullet
{
    public int X, Y;
    private readonly int dx, dy;
    public int Damage = 10;

    public Bullet(int x, int y, int dx, int dy)
    { X = x; Y = y; this.dx = dx; this.dy = dy; }

    public void Move() { X += dx; Y += dy; }
}

// ----------------- JUEGO -----------------
class Game
{
    // Mapa 10x20
    readonly char[,] layout = {
        { '#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#' },
        { '#',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ','#' },
        { '#',' ','#','#','#',' ','#','#','#',' ','#','#','#',' ','#','#','#',' ','#','#' },
        { '#',' ','#',' ','#',' ','#',' ','#',' ','#',' ','#',' ','#',' ','#',' ','#','#' },
        { '#',' ','#',' ',' ',' ','#',' ',' ',' ','#',' ',' ',' ','#',' ','#',' ',' ','#' },
        { '#',' ','#','#','#',' ','#','#','#',' ','#','#','#',' ','#','#','#',' ',' ','#' },
        { '#',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ','#' },
        { '#','#','#','#','#',' ','#','#','#','#','#','#',' ','#','#','#','#','#',' ','#' },
        { '#',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ',' ','E','#' },
        { '#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#','#' },
    };

    Player player;
    readonly List<Monster> monsters = new List<Monster>();
    readonly List<Bullet> bullets = new List<Bullet>();

    bool gameOver = false, win = false, playerMoved = false;
    readonly object lockObject = new object();

    int monsterCounter = 0;
    int monsterSpeed = 3; // 1 = rápido, 3 = normal, 5 = lento

    string lastError = ""; // para mostrar errores en HUD

    public Game()
    {
        player = new Player(1, 1);
        monsters.Add(new ChaserMonster(10, 5));
        monsters.Add(new PatrolMonster(5, 8, 3, 17));
    }

    public void Run()
    {
        Console.CursorVisible = false;
        TryFitConsole();

        var inputThread = new Thread(InputHandler) { IsBackground = true };
        inputThread.Start();

        try
        {
            while (!gameOver && !win)
            {
                lock (lockObject)
                {
                    UpdateLogic();
                    Render();
                }
                Thread.Sleep(120);
            }
        }
        catch (Exception ex)
        {
            lastError = ex.GetType().Name + ": " + ex.Message;
            gameOver = true;
            Console.Clear();
            Render(); // mostrar el HUD con el error
        }

        Console.WriteLine();
        Console.WriteLine(win
            ? "🎉 ¡Felicidades! Llegaste a la salida."
            : "💀 El monstruo te atrapó. Game Over.");
        if (!string.IsNullOrEmpty(lastError))
            Console.WriteLine("Detalle: " + lastError);

        Console.WriteLine("\nPresiona cualquier tecla para salir...");
        Console.ReadKey(true);
    }

    void TryFitConsole()
    {
        // Intento de ajustar el tamaño para evitar SetCursorPosition fuera de rango en algunas consolas
        try
        {
            int needW = Math.Max(40, layout.GetLength(1) + 2);
            int needH = Math.Max(20, layout.GetLength(0) + 6);

            if (Console.BufferWidth < needW || Console.BufferHeight < needH)
                Console.SetBufferSize(Math.Max(needW, Console.BufferWidth), Math.Max(needH, Console.BufferHeight));
            if (Console.WindowWidth < needW || Console.WindowHeight < needH)
                Console.SetWindowSize(
                    Math.Min(needW, Console.LargestWindowWidth),
                    Math.Min(needH, Console.LargestWindowHeight));
        }
        catch { /* Ignorar si la terminal no lo permite */ }
    }

    void UpdateLogic()
    {
        // Mover monstruos solo después del primer movimiento del jugador
        if (playerMoved)
        {
            monsterCounter++;
            if (monsterCounter >= monsterSpeed)
            {
                foreach (var m in monsters)
                    if (m.IsAlive) m.Update(player, layout);
                monsterCounter = 0;
            }
        }

        // Mover balas + colisiones
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            bullets[i].Move();

            // fuera del mapa o pared
            if (bullets[i].Y < 0 || bullets[i].Y >= layout.GetLength(0) ||
                bullets[i].X < 0 || bullets[i].X >= layout.GetLength(1) ||
                layout[bullets[i].Y, bullets[i].X] == '#')
            {
                bullets.RemoveAt(i);
                continue;
            }

            // impacto con monstruos
            for (int mi = 0; mi < monsters.Count; mi++)
            {
                var m = monsters[mi];
                if (m.IsAlive && bullets[i].X == m.X && bullets[i].Y == m.Y)
                {
                    m.TakeDamage(bullets[i].Damage);
                    if (!m.IsAlive) player.Stats["kills"]++;
                    bullets.RemoveAt(i);
                    break;
                }
            }
        }

        // Colisión jugador-monstruos (solo vivos)
        foreach (var m in monsters)
        {
            if (m.IsAlive && player.X == m.X && player.Y == m.Y)
            {
                gameOver = true;
                break;
            }
        }

        // Llegó a la salida
        if (layout[player.Y, player.X] == 'E')
            win = true;
    }

    void Render()
    {
        Console.SetCursorPosition(0, 0);

        // Construimos un frame buffer para evitar SetCursorPosition por cada cosa
        int h = layout.GetLength(0), w = layout.GetLength(1);
        var frame = new char[h, w];

        // Copiar el mapa base
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                frame[y, x] = layout[y, x];

        // Dibujar entidades
        foreach (var m in monsters) m.DrawInto(frame);
        player.DrawInto(frame);

        // Dibujar balas (solo si están dentro)
        foreach (var b in bullets)
        {
            if (b.Y >= 0 && b.Y < h && b.X >= 0 && b.X < w)
                frame[b.Y, b.X] = '*';
        }

        // Volcar el frame
        var sb = new StringBuilder();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) sb.Append(frame[y, x]);
            sb.AppendLine();
        }
        Console.Write(sb.ToString());

        // HUD
        Console.WriteLine($"HP Player: {player.HP} | Kills: {player.Stats["kills"]} | Shots: {player.Stats["shotsFired"]}");
        Console.WriteLine($"Monstruos vivos: {monsters.FindAll(m => m.IsAlive).Count} | Velocidad monstruos: {monsterSpeed} (↓ más lento / ↑ más rápido)");
        if (!string.IsNullOrEmpty(lastError)) Console.WriteLine("⚠️ Error: " + lastError);
        Console.WriteLine("Controles: W/A/S/D moverse | Espacio disparar | ↑/↓ ajusta velocidad");
    }

    void InputHandler()
    {
        try
        {
            while (!gameOver && !win)
            {
                var key = Console.ReadKey(true).Key;
                lock (lockObject)
                {
                    int nx = player.X, ny = player.Y;

                    switch (key)
                    {
                        case ConsoleKey.W: ny--; player.lastDx = 0;  player.lastDy = -1; break;
                        case ConsoleKey.S: ny++; player.lastDx = 0;  player.lastDy = 1;  break;
                        case ConsoleKey.A: nx--; player.lastDx = -1; player.lastDy = 0;  break;
                        case ConsoleKey.D: nx++; player.lastDx = 1;  player.lastDy = 0;  break;

                        case ConsoleKey.UpArrow:
                            monsterSpeed = Math.Max(1, monsterSpeed - 1);
                            continue;
                        case ConsoleKey.DownArrow:
                            monsterSpeed = Math.Min(9, monsterSpeed + 1);
                            continue;

                        case ConsoleKey.Spacebar:
                            bullets.Add(new Bullet(player.X + player.lastDx, player.Y + player.lastDy, player.lastDx, player.lastDy));
                            player.Stats["shotsFired"]++;
                            continue;

                        default:
                            continue;
                    }

                    // Movimiento seguro
                    if (nx >= 0 && nx < layout.GetLength(1) &&
                        ny >= 0 && ny < layout.GetLength(0) &&
                        layout[ny, nx] != '#')
                    {
                        player.MoveTo(nx, ny);
                        playerMoved = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Si algo truena aquí, lo mostramos en el HUD en el hilo principal
            lastError = ex.GetType().Name + ": " + ex.Message;
            gameOver = true;
        }
    }
}
