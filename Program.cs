using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Tesseract;
using System.Media;

class Program
{
    // ====== 설정 ======
    static Rectangle _chatArea;

    const string ReinforceText = "@플레이봇 강화";
    const string SellText = "@플레이봇 판매";

    //const int TargetLevel = 20;

    static int targetLevelSsalMuk = 15;   // 쌀먹 모드 기본값 (아무 의미 없음, 사용자 입력으로 바뀜)
    static int targetLevelChallenge = 20; // 도전 모드 기본값


    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();   // ★ DPI 인식용

    struct POINT { public int X; public int Y; }

    static int recaptureFailCount = 0;
    static int captureOffsetDir = -1; // -1=왼쪽, +1=오른쪽

    enum ReinforceResult
    {
        Unknown,
        Success,
        Destroy,
        Keep
    }

    enum RunMode
    {
        SsalMuk,
        Challenge20
    }

    // ====== Main ======
    [STAThread]
    static void Main(string[] args)
    {
        // ★ DPI 스케일링 무효화 (좌표 = 실제 픽셀 좌표로 맞춤)
        SetProcessDPIAware();

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        RunMode mode = SelectRunMode();
        if (mode == RunMode.SsalMuk)
        {
            Console.Write("쌀먹 모드의 최대 강화 목표 레벨을 입력하세요 (예: 11): ");
            if (int.TryParse(Console.ReadLine(), out int lvl))
                targetLevelSsalMuk = lvl;
            Console.WriteLine($"[INFO] 쌀먹 모드 최대 강화 레벨 = {targetLevelSsalMuk}");
        }
        else // Challenge20 모드
        {
            Console.Write("도전 모드의 목표 강화 레벨을 입력하세요 (예: 20): ");
            if (int.TryParse(Console.ReadLine(), out int lvl))
                targetLevelChallenge = lvl;
            Console.WriteLine($"[INFO] 도전 모드 목표 강화 레벨 = {targetLevelChallenge}");
        }
        Thread.Sleep(800);
        Console.WriteLine($"\n[INFO] 선택한 모드: {mode}");
        Thread.Sleep(800);

        Console.WriteLine("\n카카오톡 대화 캡쳐 영역을 잡자.\n");
        CalibrateChatArea();

        Console.WriteLine();
        Console.WriteLine($"캡쳐 영역: X={_chatArea.X}, Y={_chatArea.Y}, W={_chatArea.Width}, H={_chatArea.Height}");
        Console.WriteLine("ESC 누르면 즉시 종료됨.");
        Console.WriteLine();

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
            bool exitProgram = false;

            while (!exitProgram)
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                {
                    Console.WriteLine("ESC 입력 → 종료.");
                    break;
                }

                bool handledThisTurn = false;

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    bool withDelay = (attempt == 0);

                    using (Bitmap bmp = CaptureRegion(_chatArea, withDelay))
                    {
                        string rawText = OcrBitmap(bmp, engine, out float conf);

                        Console.Clear();
                        Console.WriteLine("===== OCR 원본 =====");
                        Console.WriteLine(rawText);
                        Console.WriteLine("====================");
                        Console.WriteLine($"신뢰도: {conf:F1}% (시도 {attempt + 1}/2)\n");
                        
                        if (IsGoldLack(rawText))
                        {
                            Console.WriteLine("[INFO] 골드 부족 카드 감지");

                            if (mode == RunMode.SsalMuk)
                            {
                                // 쌀먹 모드 → 자동 판매
                                Console.WriteLine("[MODE] 쌀먹 → 골드 부족이므로 판매 명령 전송");
                                SendChatLikeHuman(SellText);
                            }
                            else // RunMode.Challenge20
                            {
                                // 도전 모드 → 프로그램 종료
                                Console.WriteLine("[MODE] 도전모드 → 골드 부족, 프로그램 종료");
                                exitProgram = true;
                            }

                            handledThisTurn = true;
                            break;   // 이번 attempt 루프 종료
                        }
                        
                        if (IsAmbiguous(rawText, conf, mode))
                        {
                            Console.WriteLine("[INFO] 인식이 애매함 → 재캡쳐 시도");
                            if (attempt == 0)
                                continue;
                            else
                            {
                                Console.WriteLine("[WARN] 두 번 모두 인식 불확실 → 이번 턴은 명령어 전송 안 함");
                                handledThisTurn = true;

                                // === 캡처 위치 자동 미세 조정 ===
                                recaptureFailCount++;

                                if (recaptureFailCount >= 2)
                                {
                                    SystemSounds.Exclamation.Play();

                                    //// 캡처 범위 1픽셀 이동
                                    //_chatArea.X += captureOffsetDir * 1;

                                    //Console.WriteLine($"[INFO] 캡쳐 위치 이동: X={_chatArea.X} (dir={captureOffsetDir})");

                                    //// 방향 반전 처리
                                    //captureOffsetDir *= -2;
                                    
                                    //캡쳐범위 다시
                                    CalibrateChatArea();
                                    Console.WriteLine();
                                    Console.WriteLine($"새 캡쳐 영역: X={_chatArea.X}, Y={_chatArea.Y}, W={_chatArea.Width}, H={_chatArea.Height}");
                                    
                                    // 실패 카운트 초기화
                                    recaptureFailCount = 0;
                                    //using (Bitmap test = CaptureRegion(_chatArea, withDelay: false))
                                    //{
                                    //    test.Save("debug_capture.png", System.Drawing.Imaging.ImageFormat.Png);
                                    //    Console.WriteLine("debug_capture.png 로 캡쳐 저장됨");
                                    //}
                                }
                                break;
                            }
                        }

                        // 애매하지 않은 경우 → 기존 로직
                        if (mode == RunMode.SsalMuk)
                        {
                            bool shouldSell = DecideBasedOnWholeText(rawText);
                            string sendText = shouldSell ? SellText : ReinforceText;

                            Console.WriteLine($"[MODE] 쌀먹 → {(shouldSell ? "판매" : "강화")}");
                            Console.WriteLine($"입력할 문구: {sendText}");
                            SendChatLikeHuman(sendText);
                        }
                        else // Challenge20
                        {
                            bool reached = IsTargetLevelReached(rawText);

                            if (reached)
                            {
                                Console.WriteLine($"[MODE] 도전모드 → {targetLevelChallenge}강 찍힘! 자동 종료.");
                                exitProgram = true;
                                handledThisTurn = true;
                                break;
                            }

                            Console.WriteLine($"[MODE] 도전모드 → 아직 {targetLevelChallenge}강 미달. 강화 계속 감.");
                            Console.WriteLine($"입력할 문구: {ReinforceText}");
                            SendChatLikeHuman(ReinforceText);
                        }

                        handledThisTurn = true;
                        break;
                    }
                }

                if (exitProgram) break;

                if (!handledThisTurn)
                    Console.WriteLine("[INFO] 이번 턴은 처리하지 않음 (모든 시도 애매)");

                Thread.Sleep(300);
            }
        }
    }

    // ====== 모드 선택 ======
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

    // ====== 캘리브레이션 (드래그로 선택) ======
    static void CalibrateChatArea()
    {
        Console.WriteLine("===== 캡쳐 영역 설정 =====");
        Console.WriteLine("마우스로 화면을 드래그해서 '카톡 카드가 나오는 영역'을 감싸서 선택하세요.");
        Console.WriteLine("선택 중 ESC를 누르면 취소됩니다.\n");

        using (var selForm = new CaptureSelectionForm())
        {
            var result = selForm.ShowDialog();

            if (result == DialogResult.OK && !selForm.SelectedRectScreen.IsEmpty)
            {
                _chatArea = selForm.SelectedRectScreen;
                Console.WriteLine($"선택된 캡쳐 영역: X={_chatArea.X}, Y={_chatArea.Y}, W={_chatArea.Width}, H={_chatArea.Height}");
            }
            else
            {
                Console.WriteLine("영역 선택이 취소되었거나 잘못되었습니다. 프로그램을 종료합니다.");
                Thread.Sleep(1000);
                Environment.Exit(0);
            }
        }
        //using (Bitmap test = CaptureRegion(_chatArea, withDelay: false))
        //{
        //    test.Save("debug_capture.png", System.Drawing.Imaging.ImageFormat.Png);
        //    Console.WriteLine("debug_capture.png 로 캡쳐 저장됨");
        //    Console.ReadKey();
        //}
    }

    // ====== 캡쳐 + OCR ======
    static Bitmap CaptureRegion(Rectangle region, bool withDelay = true)
    {
        Bitmap bmp = new Bitmap(region.Width, region.Height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            if (withDelay)
                Thread.Sleep(3000); // 카드 뜰 시간

            g.CopyFromScreen(region.Location, Point.Empty, region.Size);
        }
        return bmp;
    }

    static bool IsAmbiguous(string rawText, float confidence, RunMode mode)
    {
        if (confidence < 50f) return true;

        ReinforceResult result = GetReinforceResult(rawText);

        if (result == ReinforceResult.Unknown) return true;

        if (result == ReinforceResult.Success)
        {
            if (!TryParseSuccessInfo(rawText, out int level, out string itemName))
                return true;
        }

        return false;
    }

    static string OcrBitmap(Bitmap bmp, TesseractEngine engine, out float confidence)
    {
        //using (var processed = new Bitmap(bmp.Width, bmp.Height))
        //{
        //    for (int y = 0; y < bmp.Height; y++)
        //    {
        //        for (int x = 0; x < bmp.Width; x++)
        //        {
        //            Color c = bmp.GetPixel(x, y);
        //            int luminance = (int)(c.R * 0.299 + c.G * 0.587 + c.B * 0.114);
        //            int v = luminance < 160 ? 0 : 255;
        //            processed.SetPixel(x, y, Color.FromArgb(v, v, v));
        //        }
        //    }

        //    using (var ms = new MemoryStream())
        //    {
        //        processed.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        //        byte[] data = ms.ToArray();

        //        using (var pix = Pix.LoadFromMemory(data))
        //        using (var page = engine.Process(pix))
        //        {
        //            string text = page.GetText();
        //            confidence = page.GetMeanConfidence() * 100;
        //            return text;
        //        }
        //    }
        //}
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            using (var pix = Pix.LoadFromMemory(ms.ToArray()))
            using (var page = engine.Process(pix))
            {
                string text = page.GetText();
                confidence = page.GetMeanConfidence() * 100;
                return text;
            }
        }
    }

    // ====== 문자열 전처리 ======
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

    // ====== 강화 결과 구분 ======
    static ReinforceResult GetReinforceResult(string rawText)
    {
        var rawLines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in rawLines)
        {
            if (!rawLine.Contains("@"))
                continue;

            string headerNorm = NormalizeDigits(NormalizeOCR(rawLine));
            string headerFlat = Regex.Replace(headerNorm, @"\s+", "");

            if (headerFlat.Contains("검판매"))
            {
                Console.WriteLine("[DEBUG] 판매 카드(검 판매) 감지 → Keep 처리");
                return ReinforceResult.Keep;
            }

            if (headerFlat.Contains("강화파괴"))
            {
                Console.WriteLine("[DEBUG] 강화 파괴 카드 감지 → Destroy 처리");
                return ReinforceResult.Destroy;
            }
        }

        string norm = NormalizeDigits(NormalizeOCR(rawText));
        string flat = Regex.Replace(norm, @"\s+", "");

        if (flat.Contains("레벨이유지되었습니다") ||
            flat.Contains("레벨이유지되었습") ||
            flat.Contains("레벨이유지되었") ||
            Regex.IsMatch(flat, "유.?지되.?었"))
        {
            return ReinforceResult.Keep;
        }

        if (Regex.IsMatch(flat, "파.?괴되.?었") ||
            Regex.IsMatch(flat, "파.?괴되었습") ||
            flat.Contains("소멸되었습니다") ||
            flat.Contains("소멸되었습") ||
            flat.Contains("부서졌"))
        {
            return ReinforceResult.Destroy;
        }

        bool hasAcquire = Regex.IsMatch(flat, "획.?득");
        bool hasPlusNum = Regex.IsMatch(flat, @"\+\d+");
        if (hasAcquire && hasPlusNum)
            return ReinforceResult.Success;

        return ReinforceResult.Unknown;
    }

    // ====== 성공 카드에서 레벨/아이템명 추출 ======
    static bool TryParseSuccessInfo(string rawText, out int level, out string itemName)
    {
        level = 0;
        itemName = null;

        string normAll = NormalizeDigits(NormalizeOCR(rawText));
        var lines = normAll.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i];
            string flatLine = Regex.Replace(line, @"\s+", "");

            if (!Regex.IsMatch(flatLine, "획.?득"))
                continue;

            string norm = Regex.Replace(line, @"^.*획\s*득\s*검\s*[:\s]*", "");

            Match m = Regex.Match(norm, @"\[\s*\+(\d+)\s*\]\s+(.+)");
            if (!m.Success)
                m = Regex.Match(norm, @"\+(\d+)\s+(.+)");

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
        return flat.Contains("검") || flat.Contains("몽둥이") || flat.Contains("교향곡") 
            || flat.Contains("선율") || flat.Contains("막대기") || flat.Contains("맹아") 
            || flat.Contains("앙심") || flat.Contains("갈증") || flat.Contains("복수")
            || flat.Contains("망아");
    }

    // ====== 쌀먹 모드 판매/강화 결정 ======
    static bool DecideBasedOnWholeText(string rawText)
    {
        ReinforceResult result = GetReinforceResult(rawText);
        Console.WriteLine($"[DEBUG] ResultType = {result}");

        if (result == ReinforceResult.Destroy || result == ReinforceResult.Keep)
            return false;

        if (result != ReinforceResult.Success)
            return false;

        if (!TryParseSuccessInfo(rawText, out int level, out string itemName))
        {
            Console.WriteLine("[DEBUG] 성공이긴 한데 파싱 실패 → 강화로");
            return false;
        }

        // 항상 강화 예외 먼저 검사
        if (IsAlwaysReinforceWeapon(itemName))
        {
            Console.WriteLine($"[DEBUG] 항상 강화 대상 무기 감지 → \"{itemName}\" 는 판매하지 않음");
            return false;   // false = 강화
        }

        bool isMainWeapon = IsMainWeapon(itemName);

        Console.WriteLine($"[DEBUG] 레벨={level}, 아이템=\"{itemName}\", 무기여부={isMainWeapon}");

        if (isMainWeapon)
        {
            return level >= targetLevelSsalMuk;
        }
        else
        {
            return level >= targetLevelSsalMuk;
        }
    }

    // ====== 도전 모드 목표 달성 ======
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
        return level >= targetLevelChallenge;
    }

    // ====== 카톡 입력 ======
    static void SendChatLikeHuman(string msg)
    {
        SendKeys.SendWait(msg);
        SendKeys.SendWait("{ENTER}");
    }
    static bool IsGoldLack(string rawText)
    {
        // 기존 전처리 재사용
        string norm = NormalizeDigits(NormalizeOCR(rawText));
        string flat = Regex.Replace(norm, @"\s+", ""); // 공백 제거

        // 기본 대사: "골드가 부족해. 골드를 더 모으고 오시게나."
        // OCR 때문에 공백/마침표가 섞일 수 있어 느슨하게 체크
        if (flat.Contains("골드가부족") || flat.Contains("골드부족"))
            return true;

        // 혹시 다른 패턴도 쓰고 싶으면 아래에 추가하면 됨
        // if (flat.Contains("필요골드") && flat.Contains("남은골드")) ...

        return false;
    }
    static bool IsAlwaysReinforceWeapon(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return false;

        string flat = Regex.Replace(itemName, @"\s+", "");

        // 이름에 '광선검' 또는 '단검'이 포함되면 항상 강화
        if (flat.Contains("광선검") || flat.Contains("단검"))
            return true;

        return false;
    }
}
