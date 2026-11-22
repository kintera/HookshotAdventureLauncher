// Authored tutorial fixtures; no game data.
using HookshotAdventure.Core;
using System.IO.Compression;
using System.Security.Cryptography;
static class TutorialFixtures
{
    public static void Check()
    {
        {
            using var stream = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJylU8tKw0AUvX2JFV2IQkER6sKFu9a9oH1SbIvQWHFjGJKhHTrN1DQprkR/xg/wA/wAdy7cuxFc+gXGpL0TdNoE0bPImfuaOXPnplSrdmtNAEgDeJ8whwTy0XxoitsI/2+Bp4dITr8p5NARChkh59B/Eyb+DY8R9S8RfuleTy2O70b4ZR/rKdUTj9MwPzmtuIjYX+IsLVeeF5xxmY7LBtiE9sjQT8SA2UwvFAt6uc+4CZVGOR/X2XMleKXYOajQgdvpM8pNXXMdYTPCNcIHsAJtYQ/R+A+S0erfUE0Gm7UD1Qm1HM1mvR61O9RpCZPypjBbxHIJhyzGA0lrUCdDWiEOOTZ82bCPe2yre8i0khAclmbRglwU/dbPCu+w/gN5Dx/kEDnodNybPivBV8V+V+zVUH/bZ8hAl3A34IZl0muff2IZZWwt7KSuUWL0QdWXwP9enRDPC4YOZBMO/MTZlE/wlIdvl46b/3tlarPQouMx6dGGOXeDJ8zdwMHKG8KaUHtMHCYs+ALBrXrz")), CompressionMode.Decompress);
            using var output = new MemoryStream(); stream.CopyTo(output);
            var patched = HAL_ConversationSkips.PatchTutorial(output.ToArray(), "Npc_Kokiri_010_Child");
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "5F6F7179D637364E4D370E7DCFCA53CD3C41A9C408AB7FCC6E2251E927B3D84C", "tutorial patch matches Python");
        }
        {
            using var stream = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJylUstKw0AUvfYhVnQhCgVFqAsX7lr3gvZJsS1CY8WNYUiGdug0U9OkuJL6M36AH+AHuHPh3o3g0i8wJu2doNMmiJ7F3LmvmTNnbrFa6VQbAJAC8D5hDktoj+dTU0wi4r8F3h4iMV2TaMNASGSINovx27Dwb3iM6H+JiMvwRnJxfi8iLnWsJdVIPM7C+sS04zLifInzlNx5XnDHVSquGmALWkNDPxV9ZjM9X8jrpR7jJpTrpVycshdK8lrxs1CmfbfdY5SbuuY6wmaEa4T3YRVawh6g8x8kotm/IZs0irULlTG1HM1m3S6129RpCpPyhjCbxHIJhwzmA0rrUCMDWiYOOTF82nCAZ+yoZ8iyohAclmfZvNwUfOlnjXfY/4F2Hz/kCG2gdNyfPivJV8V/V/y1kH/Lt5CGDuFuYOuWSW98+xMrSGN7oZK6RonRgwX8JsGiTojnBUMHUoRDX4TZlI/xlodvj46b/3tlajPQpKMR6dK6OfeCJ6zdxMHKGcIaU3tEHCYs+ALBAnry")), CompressionMode.Decompress);
            using var output = new MemoryStream(); stream.CopyTo(output);
            bool rejected = false;
            try { HAL_ConversationSkips.PatchTutorial(output.ToArray(), "Npc_Kokiri_010_Child"); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "tutorial patch rejects false completion");
        }
        {
            using var stream = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJydVL1OwzAQvv4hFdEJEANLZ0SlloEZ+hMUtQSkVGVBqqzUtFHTOHLdAgtiYmJDvAIbT8PMA8ADMDAQ0uQSFZeEn2/IZ393Z5/v7FSVRkdpAUAWwP2ABaSQ9xZNPq5j9N8Cd4+Q9r8Z5EiIEnGQN1C/ixz/h6eY+NcYPZR3M9/blRg9rKOVkZVkTCL/tB9xFbN+iNtsOHLd2R732SRvgDXQHKPbZEOTm91ypdKtDUyrB3W1Vkyq7IlkvJHm2xAsqTBOx2J+fV0wR7UVzmxxdKYPmNMtl2EZNMZHxGoTa5ic7w9Ixx/pHVPcwgqWoDGlttCOa/q5KYxBm44cxgk3rcsWY0O12fHGRJjM1gXhAvKBv59iAQ7IiNaJIPuGYBw0XHMTfbjZ71OuUxG6VRmzYCmwlsNBxetPEPiA8evYrVNkgTxrR1Lj3yRjTup6QZqvRPlrHkMOOsSazFi1e/TC468oYfgfuypnnMLfhXyxXHd2VyEsy47nGDyOR9z3Za4MSc/mWTpmHg7peEz6VO0tnCmFwipevaLB7CnlY7/j8Ak12o/i")), CompressionMode.Decompress);
            using var output = new MemoryStream(); stream.CopyTo(output);
            var patched = HAL_ConversationSkips.PatchTutorial(output.ToArray(), "Npc_Kokiri_011_Child");
            AssertTest.True(Convert.ToHexString(SHA256.HashData(patched)) == "C1D1C18EFE2651F13F351D58456D1DD3A43CF24C64AE756EE9E3C77449E9DA44", "tutorial patch matches Python");
        }
        {
            using var stream = new ZLibStream(new MemoryStream(Convert.FromBase64String("eJydUztPwzAQvr6QiugEiIGlM6JSy8AMfQRFLQEpVVmQKis1bdQ0jly3wII6MbEh/gIbv4aZHwA/gIGBkMclKmkTHt/gz/7uzj7f2VWp0ZFaAJAFsD9hASnkg0WTh1mM/lvg6SHS3phBDoUwEQt5C/X70PF/eI6Jf4vRA3k/s9wuxehBHY1MVEnGJPRPexE3MfsHuMsGM9t2z3jIJnkDbIBiad0mG+pc75YrlW5toBs9qMu1YlJlzyLG28h6F/wtJcbpWMzvrwpmyabEmSlOLtQBs7rlMqyCwviIGG1iDJPz/QHp+Ct9YIo7WMESNKbUFMppTb3UhTZo05HFOOG6cd1ibCg3O86cCJ2ZqiBcQN7391IswBEZ0ToR5FATjIOCe26jD9f7fcpVKgK3KmMGrPjWcjCpOP3xAx8xfhO7dY4skN12JDX+PWLMRbpeiKzXwvwVhyEHHWJMXJbNHr1y+DtKGP7Hri7JeOYO0Ydl2+5bhaAse05Z/M/xhOe+zpUh6du8RK6Zh2M6HpM+lXsLd0qhsI5Pr6gxc0r52Os4fAE1L4/h")), CompressionMode.Decompress);
            using var output = new MemoryStream(); stream.CopyTo(output);
            bool rejected = false;
            try { HAL_ConversationSkips.PatchTutorial(output.ToArray(), "Npc_Kokiri_011_Child"); } catch (InvalidDataException) { rejected = true; }
            AssertTest.True(rejected, "tutorial patch rejects false completion");
        }
    }
}
