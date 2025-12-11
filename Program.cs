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
    // ====== 설정 ======
    // 캡쳐할 카톡 대화 영역 저장용
    static Rectangle _chatArea;

    // 자동으로 입력될 명령어
    const string ReinforceText = "@플레이봇 강화";
    const string SellText = "@플레이봇 판매";

    // 20강 도전 모드 목표 강화 단계
    const int TargetLevel = 20;

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    struct POINT { public int X; public int Y; }

    // OCR로 나온 결과가 뭔지 구분하는 용도
    enum ReinforceResult
    {
        Unknown,
        Success,
        Destroy,
        Keep
    }

    // 프로그램이 동작할 모드
    enum RunMode
    {
        SsalMuk,      // 쌀먹 모드 (무기 종류 보고 판매/강화 결정)
        Challenge20   // 목표 레벨까지 무조건 강화
    }

    // ====== Main ======
    [STAThread]
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 프로그램 시작하자마자 모드 먼저 선택하게 한다
        RunMode mode = SelectRunMode();
        Console.WriteLine($"\n[INFO] 선택한 모드: {mode}");
        Thread.Sleep(800);

        Console.WriteLine("\n카카오톡 대화 캡쳐 영역을 잡자.\n");
        CalibrateChatArea();

        Console.WriteLine();
        Console.WriteLine($"캡쳐 영역: X={_chatArea.X}, Y={_chatArea.Y}, W={_chatArea.Width}, H={_chatArea.Height}");
        Console.WriteLine("ESC 누르면 즉시 종료됨.");
        Console.WriteLine();

        // tessdata 위치 찾기 (OCR 구동하려면 필수)
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string tessDataPath = Path.Combine(baseDir, "tessdata");

        if (!Directory.Exists(tessDataPath))
        {
            Console.WriteLine($"[오류] tessdata 폴더 없음: {tessDataPath}");
            Console.WriteLine("exe 옆에 tessdata/kor.traineddata, eng.traineddata 넣어야 실행됨.");
            Console.ReadKey();
            return;
        }

        using (var engine = new TesseractEngine(tessDataPath, "kor+eng", EngineMode.Default))
        {
            while (true)
            {
                // ESC 눌렀으면 바로 종료
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                {
                    Console.WriteLine("ESC 입력 → 종료.");
                    break;
                }

                using (Bitmap bmp = CaptureRegion(_chatArea))
                {
                    // OCR 돌려서 현재 카톡 상태 읽어옴
                    string rawText = OcrBitmap(bmp, engine, out float conf);

                    Console.Clear();
                    Console.WriteLine("===== OCR 원본 =====");
                    Console.WriteLine(rawText);
                    Console.WriteLine("====================");
                    Console.WriteLine($"신뢰도: {conf:F1}%\n");

                    if (mode == RunMode.SsalMuk)
                    {
                        // 쌀먹 모드 → 아이템 종류 기반으로 판매/강화 판단
                        bool shouldSell = DecideBasedOnWholeText(rawText);
                        string sendText = shouldSell ? SellText : ReinforceText;

                        Console.WriteLine($"[MODE] 쌀먹 → {(shouldSell ? "판매" : "강화")}");
                        Console.WriteLine($"입력할 문구: {sendText}");
                        SendChatLikeHuman(sendText);
                    }
                    else // 도전 모드
                    {
                        // 목표 레벨 찍었는지 확인
                        bool reached = IsTargetLevelReached(rawText);

                        if (reached)
                        {
                            Console.WriteLine($"[MODE] 도전모드 → {TargetLevel}강 찍힘! 자동 종료.");
                            break;
                        }

                        Console.WriteLine($"[MODE] 도전모드 → 아직 {TargetLevel}강 미달. 강화 계속 감.");
                        Console.WriteLine($"입력할 문구: {ReinforceText}");
                        SendChatLikeHuman(ReinforceText);
                    }
                }

                // 너무 빠르게 다음 루프 돌지 않게 텀 주기
                Thread.Sleep(300);
            }
        }
    }

    // ====== 모드 선택 ======
    // 여기서 사용자한테 어떤 방식으로 돌릴지 선택시키는 함수
    static RunMode SelectRunMode()
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("============== 카톡 자동 강화 프로그램 ==============");
            Console.WriteLine();
            Console.WriteLine("모드를 선택하세요.");
            Console.WriteLine();
            Console.WriteLine("  1. 쌀먹 모드");
            Console.WriteLine("     - 검/몽둥이면 1강 이상이면 판매");
            Console.WriteLine("     - 기타 아이템은 11강 이상이면 판매");
            Console.WriteLine();
            Console.WriteLine("  2. 도전 모드");
            Console.WriteLine("     - 아이템 종류 상관없이");
            Console.WriteLine("       목표 강화(+20 등) 찍을 때까지 무한 강화");
            Console.WriteLine();
            Console.WriteLine("=====================================================");
            Console.Write("번호 입력 (1 또는 2): ");

            string input = Console.ReadLine().Trim();

            if (input == "1") return RunMode.SsalMuk;
            if (input == "2") return RunMode.Challenge20;

            Console.WriteLine("\n잘못 입력함. 다시 입력해라 (1 또는 2).");
            Thread.Sleep(1000);
        }
    }

    // ====== 캘리브레이션 ======
    // 마우스로 대화창 범위를 직접 찍게 함
    static void CalibrateChatArea()
    {
        POINT p1, p2;

        Console.WriteLine("[1단계] 화면에서 '카톡 말풍선 카드의 왼쪽 위'에 마우스 올리고 1 누르기.");
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.KeyChar == '1')
            {
                GetCursorPos(out p1);
                Console.WriteLine($"왼쪽 위 → X={p1.X}, Y={p1.Y}");
                break;
            }
        }

        Console.WriteLine("[2단계] 이번엔 '카톡 카드 오른쪽 아래'에 마우스 올리고 2 누르기.");
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.KeyChar == '2')
            {
                GetCursorPos(out p2);
                Console.WriteLine($"오른쪽 아래 → X={p2.X}, Y={p2.Y}");
                break;
            }
        }

        // 두 점 기준으로 정확한 캡쳐 영역 계산
        int x = Math.Min(p1.X, p2.X);
        int y = Math.Min(p1.Y, p2.Y);
        int w = Math.Abs(p2.X - p1.X);
        int h = Math.Abs(p2.Y - p1.Y);

        _chatArea = new Rectangle(x, y, w, h);
    }

    // ====== 캡쳐 + OCR ======
    // 실제 화면을 잘라서 OCR에 넘긴다
    static Bitmap CaptureRegion(Rectangle region)
    {
        Bitmap bmp = new Bitmap(region.Width, region.Height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            // 너무 빨리 캡쳐하면 카드가 안 뜬 상태일 수 있으니 약간 지연
            Thread.Sleep(1500);
            g.CopyFromScreen(region.Location, Point.Empty, region.Size);
        }

        return bmp;
    }

    static string OcrBitmap(Bitmap bmp, TesseractEngine engine, out float confidence)
    {
        // Tesseract가 읽기 쉬운 흑백 이미지로 변환
        using (var processed = new Bitmap(bmp.Width, bmp.Height))
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    int luminance = (int)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);

                    // 임계값으로 흑/백만 남김
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

    // ====== 문자열 전처리 ======
    // OCR이 이상하게 읽은 글자들 보정
    static string NormalizeDigits(string s)
    {
        return s
            .Replace("l", "1")
            .Replace("I", "1")
            .Replace("O", "0");
    }

    // OCR 결과에서 불필요한 문자 제거
    static string NormalizeOCR(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            // 숫자/한글/강화문자만 남기기
            string clean = Regex.Replace(lines[i], @"[^가-힣0-9\+\[\]\:\s]", "");
            clean = Regex.Replace(clean, @"\s+", " ");
            lines[i] = clean.Trim();
        }
        return string.Join("\n", lines);
    }

    // ====== 강화 결과 구분 ======
    // OCR에서 카드 내용을 보고 성공/유지/파괴 판단
    static ReinforceResult GetReinforceResult(string rawText)
    {
        string norm = NormalizeDigits(NormalizeOCR(rawText));
        string flat = Regex.Replace(norm, @"\s+", ""); // 공백 제거

        // 유지
        if (flat.Contains("레벨이유지되었습니다") ||
            flat.Contains("레벨이유지되었습") ||
            flat.Contains("레벨이유지되었") ||
            Regex.IsMatch(flat, "유.?지되.?었"))
        {
            return ReinforceResult.Keep;
        }

        // 파괴
        if (Regex.IsMatch(flat, "파.?괴되.?었") ||
            Regex.IsMatch(flat, "파.?괴되었습") ||
            flat.Contains("소멸되었습니다") ||
            flat.Contains("소멸되었습") ||
            flat.Contains("부서졌"))
        {
            return ReinforceResult.Destroy;
        }

        // 성공 → “획득” + “+숫자” 조합이면 성공으로 본다
        bool hasAcquire = Regex.IsMatch(flat, "획.?득");
        bool hasPlusNum = Regex.IsMatch(flat, @"\+\d+");
        if (hasAcquire && hasPlusNum)
            return ReinforceResult.Success;

        // 그 외엔 몰?루
        return ReinforceResult.Unknown;
    }

    // ====== 성공 카드에서 레벨/아이템명 추출 ======
    static bool TryParseSuccessInfo(string rawText, out int level, out string itemName)
    {
        level = 0;
        itemName = null;

        string normAll = NormalizeDigits(NormalizeOCR(rawText));
        var lines = normAll.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // 가장 아래줄부터 “획득” 포함된 줄 탐색
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i];
            string flatLine = Regex.Replace(line, @"\s+", "");

            if (!Regex.IsMatch(flatLine, "획.?득"))
                continue;

            // “획득 검:” 같은 앞부분은 버림
            string norm = Regex.Replace(line, @"^.*획\s*득\s*검\s*[:\s]*", "");

            // “[+n] 이름” 패턴이 제일 정확함
            Match m = Regex.Match(norm, @"\[\s*\+(\d+)\s*\]\s+(.+)");
            if (!m.Success)
            {
                // 가끔 “[ ]” 빠진 "+n 이름" 패턴도 있다
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

    // ====== 아이템이 검/몽둥이인지 판별 ======
    static bool IsMainWeapon(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return false;

        string flat = Regex.Replace(itemName, @"\s+", "");
        return flat.Contains("검") || flat.Contains("몽둥이");
    }

    // ====== 쌀먹 모드 판매/강화 결정 ======
    static bool DecideBasedOnWholeText(string rawText)
    {
        ReinforceResult result = GetReinforceResult(rawText);
        Console.WriteLine($"[DEBUG] ResultType = {result}");

        // 파괴/유지는 무조건 강화
        if (result == ReinforceResult.Destroy || result == ReinforceResult.Keep)
            return false;

        // 성공 아니면 일단 강화
        if (result != ReinforceResult.Success)
            return false;

        // 성공인데 아이템 파싱 실패하면 강화
        if (!TryParseSuccessInfo(rawText, out int level, out string itemName))
        {
            Console.WriteLine("[DEBUG] 성공이긴 한데 파싱 실패 → 강화로");
            return false;
        }

        bool isMainWeapon = IsMainWeapon(itemName);

        Console.WriteLine($"[DEBUG] 레벨={level}, 아이템=\"{itemName}\", 무기여부={isMainWeapon}");

        if (isMainWeapon)
        {
            // 검/몽둥이는 1강 이상이면 판매
            return level >= 1;
        }
        else
        {
            // 나머지 아이템은 11강부터 판매
            return level >= 11;
        }
    }

    // ====== 도전모드: 목표 레벨 달성 여부 ======
    static bool IsTargetLevelReached(string rawText)
    {
        ReinforceResult result = GetReinforceResult(rawText);
        Console.WriteLine($"[DEBUG] ResultType = {result}");

        if (result != ReinforceResult.Success)
            return false;

        if (!TryParseSuccessInfo(rawText, out int level, out string itemName))
        {
            Console.WriteLine("[DEBUG] 성공인데 파싱 실패 → 아직 목표 X");
            return false;
        }

        Console.WriteLine($"[DEBUG] 현재 레벨 = {level}, 아이템 = \"{itemName}\"");

        return level >= TargetLevel;
    }

    // ====== 카톡 입력 ======
    // 입력 시작 전에 딜레이를 줘야 카드 뜨기 전에 입력이 들어가는 버그를 예방함
    static void SendChatLikeHuman(string msg)
    {
        // 입력을 너무 빨리 시작하면 OCR이 이전 카드 읽는 문제 있어서 지연
        Thread.Sleep(1200);

        SendKeys.SendWait(msg);
        Thread.Sleep(300); // 사람처럼 약간 멈춤
        SendKeys.SendWait("{ENTER}");
    }
}
