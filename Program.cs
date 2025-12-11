using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Tesseract;

class Program
{
    // 캡쳐할 카카오톡 대화 영역 (실행 시 마우스로 설정)
    static Rectangle _chatArea;

    const string ReinforceText = "@플레이봇 강화";
    const string SellText = "@플레이봇 판매";

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    struct POINT { public int X; public int Y; }

    enum ReinforceResult
    {
        Unknown,
        Success,
        Destroy,
        Keep
    }

    [STAThread]
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("=== 카톡 자동 강화 프로그램 ===");
        Console.WriteLine("1) 카카오톡 플레이봇 채팅창을 원하는 위치/크기로 맞춰 둡니다.");
        Console.WriteLine("2) 아래 안내에 따라 마우스로 대화 영역 좌표를 찍습니다.");
        Console.WriteLine();

        CalibrateChatArea();

        Console.WriteLine();
        Console.WriteLine($"캡쳐 영역: X={_chatArea.X}, Y={_chatArea.Y}, W={_chatArea.Width}, H={_chatArea.Height}");
        Console.WriteLine("ESC를 누르면 언제든지 종료됩니다.");
        Console.WriteLine("카카오톡 창을 맨 앞으로 두고, 입력창에 커서를 둔 뒤 기다리면 자동으로 돌기 시작합니다.");
        Console.WriteLine();

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string tessDataPath = Path.Combine(baseDir, "tessdata");

        if (!Directory.Exists(tessDataPath))
        {
            Console.WriteLine($"[오류] tessdata 폴더를 찾을 수 없습니다: {tessDataPath}");
            Console.WriteLine("실행 파일 옆에 tessdata/kor.traineddata, eng.traineddata 가 있어야 합니다.");
            Console.ReadKey();
            return;
        }

        using (var engine = new TesseractEngine(tessDataPath, "kor+eng", EngineMode.Default))
        {
            while (true)
            {
                // ESC로 종료
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        Console.WriteLine("ESC 입력. 프로그램을 종료합니다.");
                        break;
                    }
                }

                using (Bitmap bmp = CaptureRegion(_chatArea))
                {
                    string rawText = OcrBitmap(bmp, engine, out float conf);

                    Console.Clear();
                    Console.WriteLine("===== OCR 원본 =====");
                    Console.WriteLine(rawText);
                    Console.WriteLine("====================");
                    Console.WriteLine($"신뢰도: {conf:F1}%");

                    bool shouldSell = DecideBasedOnWholeText(rawText);
                    string sendText = shouldSell ? SellText : ReinforceText;

                    Console.WriteLine($"[DEBUG] 최종 판정: {(shouldSell ? "판매" : "강화")}");
                    Console.WriteLine($"보낼 텍스트: {sendText}");

                    SendChatLikeHuman(sendText);
                }

                Thread.Sleep(300);
            }
        }
    }

    // ================= 캘리브레이션 =================

    static void CalibrateChatArea()
    {
        POINT p1, p2;

        Console.WriteLine("[1단계] 마우스를 '챗봇 말풍선 카드 전체의 왼쪽 위'에 올려두고 1 키를 누르세요.");
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.KeyChar == '1')
            {
                GetCursorPos(out p1);
                Console.WriteLine($"왼쪽 위 좌표: X={p1.X}, Y={p1.Y}");
                break;
            }
        }

        Console.WriteLine("[2단계] 마우스를 '골드 / 획득 검 / 유지/파괴 문장까지 모두 포함하는 오른쪽 아래'에 올려두고 2 키를 누르세요.");
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.KeyChar == '2')
            {
                GetCursorPos(out p2);
                Console.WriteLine($"오른쪽 아래 좌표: X={p2.X}, Y={p2.Y}");
                break;
            }
        }

        int x = Math.Min(p1.X, p2.X);
        int y = Math.Min(p1.Y, p2.Y);
        int w = Math.Abs(p2.X - p1.X);
        int h = Math.Abs(p2.Y - p1.Y);

        _chatArea = new Rectangle(x, y, w, h);
    }

    // ================= 화면 캡쳐 & OCR =================

    static Bitmap CaptureRegion(Rectangle region)
    {
        Bitmap bmp = new Bitmap(region.Width, region.Height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            Thread.Sleep(1500); // 살짝 여유
            g.CopyFromScreen(region.Location, Point.Empty, region.Size);
        }

        // 디버그용으로 보고 싶으면 주석 해제
        //string debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
        //    $"debug_capture_{DateTime.Now:HHmmss}.png");
        //bmp.Save(debugPath, System.Drawing.Imaging.ImageFormat.Png);

        return bmp;
    }

    static string OcrBitmap(Bitmap bmp, TesseractEngine engine, out float confidence)
    {
        using (var processed = new Bitmap(bmp.Width, bmp.Height))
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color c = bmp.GetPixel(x, y);

                    int luminance = (int)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
                    int v = luminance < 160 ? 0 : 255;

                    processed.SetPixel(x, y, Color.FromArgb(v, v, v));
                }
            }

            using (var ms = new MemoryStream())
            {
                processed.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                byte[] data = ms.ToArray();

                using (var pix = Pix.LoadFromMemory(data))
                using (var page = engine.Process(pix))
                {
                    string text = page.GetText();
                    confidence = page.GetMeanConfidence() * 100;
                    return text;
                }
            }
        }
    }

    // ================= 전처리 유틸 =================

    static string NormalizeDigits(string s)
    {
        return s
            .Replace("l", "1")
            .Replace("I", "1")
            .Replace("O", "0");
    }

    static string NormalizeOCR(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string clean = Regex.Replace(lines[i], @"[^가-힣0-9\+\[\]\:\s]", "");
            clean = Regex.Replace(clean, @"\s+", " ");
            lines[i] = clean.Trim();
        }
        return string.Join("\n", lines);
    }

    // ================= 결과 타입 판정 =================

    static ReinforceResult GetReinforceResult(string rawText)
    {
        // 전체 텍스트 기준으로 판단
        string norm = NormalizeDigits(NormalizeOCR(rawText));
        string flat = Regex.Replace(norm, @"\s+", ""); // 공백 제거

        // 1) 유지: "레벨이 유지되었습니다" 계열
        if (flat.Contains("레벨이유지되었습니다") ||
            flat.Contains("레벨이유지되었습") ||
            flat.Contains("레벨이유지되었") ||
            Regex.IsMatch(flat, "유.?지되.?었"))
        {
            return ReinforceResult.Keep;
        }

        // 2) 파괴: "파괴되었습니다", "소멸되었습니다" 등
        if (Regex.IsMatch(flat, "파.?괴되.?었") ||
            Regex.IsMatch(flat, "파.?괴되었습") ||
            flat.Contains("소멸되었습니다") ||
            flat.Contains("소멸되었습") ||
            flat.Contains("부서졌"))
        {
            return ReinforceResult.Destroy;
        }

        // 3) 성공: "획득" + "+숫자" 패턴
        //  - OCR에서 "획 득", "홱득" 등으로 깨지는 것까지 허용
        bool hasAcquire = Regex.IsMatch(flat, "획.?득");
        bool hasPlusNum = Regex.IsMatch(flat, @"\+\d+");

        if (hasAcquire && hasPlusNum)
        {
            return ReinforceResult.Success;
        }

        return ReinforceResult.Unknown;
    }

    // ================= 성공 시 아이템 정보 파싱 =================

    static bool TryParseSuccessInfo(string rawText, out int level, out string itemName)
    {
        level = 0;
        itemName = null;

        string normAll = NormalizeDigits(NormalizeOCR(rawText));
        var lines = normAll.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // 아래쪽부터 "획득"이 들어간 줄을 찾는다
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i];
            string flatLine = Regex.Replace(line, @"\s+", "");

            if (!Regex.IsMatch(flatLine, "획.?득"))
                continue;

            // "획득 검:" 같은 prefix 제거 (여러 경우 허용)
            string norm = Regex.Replace(line, @"^.*획\s*득\s*검\s*[:\s]*", "");

            // 1) "[+11] 이름" 패턴 우선 시도
            Match m = Regex.Match(norm, @"\[\s*\+(\d+)\s*\]\s+(.+)");
            if (!m.Success)
            {
                // 2) "+11 이름" 패턴 시도
                m = Regex.Match(norm, @"\+(\d+)\s+(.+)");
            }

            if (!m.Success)
                continue;

            if (!int.TryParse(m.Groups[1].Value, out level))
                continue;

            itemName = m.Groups[2].Value.Trim();
            return true;
        }

        return false;
    }

    // ================= 무기 이름 판정 =================

    static bool IsMainWeapon(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return false;

        string flat = Regex.Replace(itemName, @"\s+", "");

        // 여기서만 무기 키워드를 검사한다.
        // "획득 검" 같은 prefix는 TryParseSuccessInfo에서 이미 제거됨.
        return flat.Contains("검") || flat.Contains("몽둥이");
    }

    // ================= 최종 판단 (판매 / 강화) =================

    // true  => 판매
    // false => 강화
    static bool DecideBasedOnWholeText(string rawText)
    {
        ReinforceResult result = GetReinforceResult(rawText);
        Console.WriteLine($"[DEBUG] ResultType = {result}");

        // 파괴 / 유지 → 무조건 강화
        if (result == ReinforceResult.Destroy || result == ReinforceResult.Keep)
            return false;

        // 성공이 아니면 안전하게 강화
        if (result != ReinforceResult.Success)
            return false;

        // 성공일 때만 "획득 검" 정보로 판단
        if (!TryParseSuccessInfo(rawText, out int level, out string itemName))
        {
            Console.WriteLine("[DEBUG] 성공인데 아이템 파싱 실패 → 강화로 처리");
            return false;
        }

        bool isMainWeapon = IsMainWeapon(itemName);

        Console.WriteLine($"[DEBUG] level = {level}, itemName = \"{itemName}\", isMainWeapon = {isMainWeapon}");

        if (isMainWeapon)
        {
            // 검/몽둥이: 1강 이상 판매
            return level >= 1;
        }
        else
        {
            // 그 외 아이템: 11강 이상 판매
            return level >= 11;
        }
    }

    // ================= 카톡에 텍스트 보내기 =================

    static void SendChatLikeHuman(string msg)
    {
        SendKeys.SendWait(msg);
        Thread.Sleep(1500);
        SendKeys.SendWait("{ENTER}");
    }
}
