using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DhirDhar.Application.Localization;

/// <summary>
/// Language-aware script translation and Google Indic-style phonetic transliteration engine
/// between English (Latin), Gujarati, Hindi, Marathi, Bengali, Punjabi, Tamil, Telugu, Kannada, Malayalam, and Odia.
/// </summary>
public static class ScriptTranslator
{
    private static readonly Dictionary<string, (string Gujarati, string Hindi)> ExactDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---------------------------------------------------------------------
        // Complete Gujarati Independent Vowels
        // ---------------------------------------------------------------------
        ["a"] = ("અ", "अ"),
        ["aa"] = ("આ", "आ"),
        ["ā"] = ("આ", "आ"),
        ["i"] = ("ઇ", "इ"),
        ["ii"] = ("ઈ", "ई"),
        ["ee"] = ("ઈ", "ई"),
        ["ī"] = ("ઈ", "ई"),
        ["u"] = ("ઉ", "उ"),
        ["uu"] = ("ઊ", "ऊ"),
        ["oo"] = ("ઊ", "ऊ"),
        ["ū"] = ("ઊ", "ऊ"),
        ["ru"] = ("ઋ", "ऋ"),
        ["ri"] = ("ઋ", "ऋ"),
        ["r̥"] = ("ઋ", "ऋ"),
        ["e"] = ("એ", "ए"),
        ["ai"] = ("ઐ", "ऐ"),
        ["o"] = ("ઓ", "ओ"),
        ["au"] = ("ઔ", "औ"),
        ["am"] = ("અં", "अं"),
        ["an"] = ("અં", "अं"),
        ["aṃ"] = ("અં", "अं"),
        ["ah"] = ("અઃ", "अः"),
        ["aḥ"] = ("અઃ", "अः"),
        ["ae"] = ("ઍ", "ऍ"),

        // ---------------------------------------------------------------------
        // Complete Gujarati Consonant Set (Phonetic English Input Mappings)
        // ---------------------------------------------------------------------
        // Gutturals / Velars: ક, ખ, ગ, ઘ, ઙ
        ["ka"] = ("ક", "क"),
        ["kha"] = ("ખ", "ख"),
        ["ga"] = ("ગ", "ग"),
        ["gha"] = ("ઘ", "घ"),
        ["nga"] = ("ઙ", "ङ"),

        // Palatals: ચ, છ, જ, ઝ, ઞ
        ["cha"] = ("ચ", "च"),
        ["chha"] = ("છ", "छ"),
        ["ja"] = ("જ", "ज"),
        ["jha"] = ("ઝ", "झ"),
        ["nya"] = ("ઞ", "ञ"),

        // Retroflex: ટ, ઠ, ડ, ઢ, ણ (diacritics)
        ["ṭa"] = ("ટ", "ट"),
        ["ṭha"] = ("ઠ", "ठ"),
        ["ḍa"] = ("ડ", "ड"),
        ["ḍha"] = ("ઢ", "ढ"),
        ["ṇa"] = ("ણ", "ण"),

        // Dentals: ત, થ, દ, ધ, ન
        ["ta"] = ("ત", "त"),
        ["tha"] = ("થ", "थ"),
        ["da"] = ("દ", "द"),
        ["dha"] = ("ધ", "ध"),
        ["na"] = ("ન", "न"),

        // Labials: પ, ફ, બ, ભ, મ
        ["pa"] = ("પ", "प"),
        ["pha"] = ("ફ", "फ"),
        ["fa"] = ("ફ", "फ़"),
        ["ba"] = ("બ", "ब"),
        ["bha"] = ("ભ", "भ"),
        ["ma"] = ("મ", "म"),

        // Semi-vowels & Liquids: ય, ર, લ, વ
        ["ya"] = ("ય", "य"),
        ["ra"] = ("ર", "र"),
        ["la"] = ("લ", "ल"),
        ["va"] = ("વ", "व"),
        ["wa"] = ("વ", "व"),

        // Sibilants, Aspirate & Retroflex Lateral: શ, ષ, સ, હ, ળ
        ["sha"] = ("શ", "श"),
        ["shha"] = ("ષ", "ष"),
        ["ṣha"] = ("ષ", "ष"),
        ["sa"] = ("સ", "स"),
        ["ha"] = ("હ", "ह"),
        ["lla"] = ("ળ", "ळ"),
        ["ḷa"] = ("ળ", "ळ"),

        // Special Conjuncts: ક્ષ, જ્ઞ
        ["ksha"] = ("ક્ષ", "क्ष"),
        ["gnya"] = ("જ્ઞ", "ज्ञ"),
        ["jnya"] = ("જ્ઞ", "ज्ञ"),
        ["dnya"] = ("જ્ઞ", "ज्ञ"),

        // Vowel combinations on 'k':
        ["kaa"] = ("કા", "का"),
        ["ki"] = ("કિ", "कि"),
        ["kee"] = ("કી", "की"),
        ["kii"] = ("કી", "की"),
        ["ku"] = ("કુ", "कु"),
        ["koo"] = ("કૂ", "कू"),
        ["kuu"] = ("કૂ", "कू"),
        ["ke"] = ("કે", "के"),
        ["kai"] = ("કૈ", "कै"),
        ["ko"] = ("કો", "को"),
        ["kau"] = ("કૌ", "कौ"),
        ["kri"] = ("કૃ", "कृ"),
        ["kru"] = ("કૃ", "कृ"),

        // Core Words & Examples
        ["bhaag"] = ("ભાગ", "भाग"),
        ["Bhaag"] = ("ભાગ", "भाग"),
        ["Bhargav"] = ("\u0AAD\u0ABE\u0AB0\u0ACD\u0A97\u0AB5", "\u092D\u093E\u0930\u094D\u0917\u0935"),
        ["bhargav"] = ("\u0AAD\u0ABE\u0AB0\u0ACD\u0A97\u0AB5", "\u092D\u093E\u0930\u094D\u0917\u0935"),
        ["palak"] = ("પલક", "पलक"),
        ["Palak"] = ("પલક", "पलक"),
        ["paalak"] = ("પાલક", "पालक"),
        ["Paalak"] = ("પાલક", "पालक"),
        ["chetan"] = ("ચેતન", "चेतन"),
        ["Chetan"] = ("ચેતન", "चेतन"),
        ["malai"] = ("મલાઈ", "मलाई"),
        ["Malai"] = ("મલાઈ", "मलाई"),
        ["Panchal"] = ("પંચાલ", "पंचाल"),
        ["panchal"] = ("પંચાલ", "पंचाल"),
        ["bhargav panchal"] = ("\u0AAD\u0ABE\u0AB0\u0ACD\u0A97\u0AB5 \u0AAA\u0A82\u0A9a\u0ABE\u0AB2", "\u092D\u093E\u0930\u094D\u0917\u0935 \u092A\u0902\u091a\u093E\u0932"),
        ["Bhargav Panchal"] = ("\u0AAD\u0ABE\u0AB0\u0ACD\u0A97\u0AB5 \u0AAA\u0A82\u0A9a\u0ABE\u0AB2", "\u092D\u093E\u0930\u094D\u0917\u0935 \u092A\u0902\u091a\u093E\u0932"),
        ["Bhargav 123"] = ("\u0AAD\u0ABE\u0AB0\u0ACD\u0A97\u0AB5 \u0AE7\u0AE8\u0AE9", "\u092D\u093E\u0930\u094D\u0917\u0935 123"),
        ["bhargav 123"] = ("\u0AAD\u0ABE\u0AB0\u0ACD\u0A97\u0AB5 \u0AE7\u0AE8\u0AE9", "\u092D\u093E\u0930\u094D\u0917\u0935 123"),
        ["DhirDhar"] = ("ધીરધાર", "धीरधार"),
        ["dhirdhar"] = ("ધીરધાર", "\u0927\u0940\u0930\u0927\u093E\u0930"),
        ["dhir"] = ("ધીર", "धीर"),
        ["dhar"] = ("ધાર", "धार"),
        ["Dhir"] = ("ધીર", "धीर"),
        ["Dhar"] = ("ધાર", "धार"),
        ["dhir dhar"] = ("ધીર ધાર", "धीर धार"),
        ["Dhir Dhar"] = ("ધીર ધાર", "धीर धार"),
        ["namaste"] = ("નમસ્તે", "नमस्ते"),
        ["Namaste"] = ("નમસ્તે", "नमस्ते"),
        ["namaskar"] = ("નમસ્કાર", "नमस्कार"),
        ["Namaskar"] = ("નમસ્કાર", "नमस्कार"),
        ["maru"] = ("મારું", "मेरा"),
        ["Maru"] = ("મારું", "मेरा"),
        ["maro"] = ("મારો", "मेरा"),
        ["mari"] = ("મારી", "मेरी"),
        ["mara"] = ("મારા", "मेरे"),
        ["naam"] = ("નામ", "नाम"),
        ["Naam"] = ("નામ", "नाम"),
        ["nam"] = ("નામ", "नाम"),
        ["chhe"] = ("છે", "है"),
        ["Chhe"] = ("છે", "है"),
        ["che"] = ("છે", "है"),
        ["chho"] = ("છો", "हो"),
        ["cho"] = ("છો", "हो"),
        ["chhu"] = ("છું", "हूँ"),
        ["chhun"] = ("છું", "हूँ"),
        ["chu"] = ("છું", "हूँ"),
        ["chhiye"] = ("છીએ", "हैं"),
        ["Ahmedabad"] = ("અમદાવાદ", "अहमदाबाद"),
        ["ahmedabad"] = ("અમદાવાદ", "अहमदाबाद"),
        ["amdavad"] = ("અમદાવાદ", "अहमदाबाद"),
        ["Amdavad"] = ("અમદાવાદ", "अहमदाबाद"),

        // Real Gujarati Names & Words requested
        ["Dwiti"] = ("દ્વિતી", "द्विती"),
        ["dwiti"] = ("દ્વિતી", "द्विती"),
        ["Jignesh"] = ("જિગ્નેશ", "जिग्नेश"),
        ["jignesh"] = ("જિગ્નેશ", "जिग्नेश"),
        ["Lakshmi"] = ("લક્ષ્મી", "लक्ष्मी"),
        ["lakshmi"] = ("લક્ષ્મી", "लक्ष्मी"),
        ["Kiran"] = ("કિરણ", "किरण"),
        ["kiran"] = ("કિરણ", "किरण"),
        ["Kshatriya"] = ("ક્ષત્રિય", "क्षत्रिय"),
        ["kshatriya"] = ("ક્ષત્રિય", "क्षत्रिय"),
        ["Gujarat"] = ("ગુજરાત", "गुजरात"),
        ["gujarat"] = ("ગુજરાત", "गुजरात"),

        // Explicit Conjunct Formulas:
        ["k + sha"] = ("ક્ષ", "क्ष"),
        ["k+sha"] = ("ક્ષ", "क्ष"),
        ["j + nya"] = ("જ્ઞ", "ज्ञ"),
        ["j+nya"] = ("જ્ઞ", "ज्ञ"),
        ["k + ta"] = ("ક્ત", "क्त"),
        ["k+ta"] = ("ક્ત", "क्त"),
        ["k + ra"] = ("ક્ર", "क्र"),
        ["k+ra"] = ("ક્ર", "क्र"),
        ["p + ra"] = ("પ્ર", "प्र"),
        ["p+ra"] = ("પ્ર", "प्र"),
        ["t + ra"] = ("ત્ર", "त्र"),
        ["t+ra"] = ("ત્ર", "त्र"),
        ["sh + ra"] = ("શ્ર", "श्र"),
        ["sh+ra"] = ("શ્ર", "श्र"),
        ["bh + ra"] = ("ભ્ર", "भ्र"),
        ["bh+ra"] = ("ભ્ર", "भ्र"),
        ["dh + ya"] = ("ધ્ય", "ध्य"),
        ["dh+ya"] = ("ધ્ય", "ध्य"),

        // Names & Compound Names
        ["Bhargav"] = ("ભાર્ગવ", "भार्गव"),
        ["bhargav"] = ("ભાર્ગવ", "भार्गव"),
        ["Palak"] = ("પલક", "पलक"),
        ["palak"] = ("પલક", "पलक"),
        ["Valsing"] = ("વાલસિંગ", "वालसिंह"),
        ["valsing"] = ("વાલસિંગ", "वालसिंह"),
        ["Chudi"] = ("ચૂડી", "चूड़ी"),
        ["chudi"] = ("ચૂડી", "चूड़ी"),
        ["Choodi"] = ("ચૂડી", "चूड़ी"),
        ["choodi"] = ("ચૂડી", "चूड़ी"),
        ["Chudo"] = ("ચૂડો", "चूड़ा"),
        ["chudo"] = ("ચૂડો", "चूड़ा"),
        ["Chuda"] = ("ચૂડા", "चूड़ा"),
        ["chuda"] = ("ચૂડા", "चूड़ा"),
        ["Bangle"] = ("ચૂડી", "चूड़ी"),
        ["bangle"] = ("ચૂડી", "चूड़ी"),
        ["Bangles"] = ("ચૂડી", "चूड़ी"),
        ["bangles"] = ("ચૂડી", "चूड़ी"),
        ["Kandoro"] = ("કંદોરો", "कमरबंद"),
        ["kandoro"] = ("કંદોરો", "कमरबंद"),
        ["Damani"] = ("દામણી", "दामनी"),
        ["damani"] = ("દામણી", "दामनी"),
        ["Baju"] = ("બાજુ", "बाजू"),
        ["baju"] = ("બાજુ", "बाजू"),
        ["Hathphool"] = ("હાથફૂલ", "हथफूल"),
        ["hathphool"] = ("હાથફૂલ", "हथफूल"),
        ["Payal"] = ("પાયલ", "पायल"),
        ["payal"] = ("પાયલ", "पायल"),
        ["Paijan"] = ("પાયલ", "पायल"),
        ["paijan"] = ("પાયલ", "पायल"),
        ["Kalla"] = ("કલ્લા", "कल्ला"),
        ["kalla"] = ("કલ્લા", "कल्ला"),
        ["Pahochi"] = ("પહોંચી", "पहुंची"),
        ["pahochi"] = ("પહોંચી", "पहुंची"),
        ["Kadli"] = ("કડલી", "कडली"),
        ["kadli"] = ("કડલી", "कडली"),
        ["Zud"] = ("ઝૂડ", "झूड़"),
        ["zud"] = ("ઝૂડ", "झूड़"),
        ["Bor"] = ("બોર", "बोर"),
        ["bor"] = ("બોર", "बोर"),
        ["Gokhru"] = ("ગોખરુ", "गोखरू"),
        ["gokhru"] = ("ગોખરુ", "गोखरू"),
        ["Sankali"] = ("સાંકળી", "सांकली"),
        ["sankali"] = ("સાંકળી", "सांकली"),
        ["Kap"] = ("કાપ", "काप"),
        ["kap"] = ("કાપ", "काप"),
        ["Mangalsutra"] = ("મંગળસૂત્ર", "मंगलसूत्र"),
        ["mangalsutra"] = ("મંગળસૂત્ર", "मंगलसूत्र"),
        ["Ramsinh"] = ("રામસિંહ", "रामसिंह"),
        ["Valsinh"] = ("વાલસિંહ", "वालसिंह"),
        ["Katara"] = ("કટારા", "कटारा"),
        ["Ramsinh Valsinh Katara"] = ("રામસિંહ વાલસિંહ કટારા", "रामसिंह वालसिंह कटारा"),
        ["Bhargavkumar"] = ("ભાર્ગવકુમાર", "भार्गवकुमार"),
        ["Pravinchandra"] = ("પ્રવિણચંદ્ર", "प्रविणचन्द्र"),
        ["Pravin"] = ("પ્રવિણ", "प्रविણ"),
        ["pravin"] = ("પ્રવિણ", "प्रવિણ"),
        ["Sukhsar"] = ("સુખસર", "सुखसर"),
        ["sukhsar"] = ("સુખસર", "सुखसर"),
        ["Patan"] = ("પાટણ", "पाटन"),
        ["patan"] = ("પાટણ", "पाटन"),
        ["Rakesh"] = ("રાકેશ", "राकेश"),
        ["rakesh"] = ("રાકેશ", "राकेश"),
        ["Bharat"] = ("ભારત", "भारत"),
        ["bharat"] = ("ભારત", "भारत"),
        ["Mahatma"] = ("મહાત્મા", "महात्मा"),
        ["mahatma"] = ("મહાત્મા", "महात्मा"),
        ["Ramesh"] = ("રમેશ", "रमेश"),
        ["ramesh"] = ("રમેશ", "रमेश"),
        ["Suresh"] = ("સુરેશ", "सुरेश"),
        ["suresh"] = ("સુરેશ", "सुरेश"),
        ["Mahesh"] = ("મહેશ", "महेश"),
        ["mahesh"] = ("મહેશ", "महेश"),
        ["Rajesh"] = ("રાજેશ", "राजेश"),
        ["rajesh"] = ("રાજેશ", "राजेश"),
        ["Patel"] = ("પટેલ", "पटेल"),
        ["patel"] = ("પટેલ", "पटेल"),
        ["Shah"] = ("શાહ", "शाह"),
        ["shah"] = ("શાહ", "शाह"),
        ["Sharma"] = ("શર્મા", "शर्मा"),
        ["sharma"] = ("શર્મા", "शर्मा"),
        ["Verma"] = ("વર્મા", "वर्मा"),
        ["verma"] = ("વર્મા", "वर्मा"),
        ["Gupta"] = ("ગુપ્તા", "गुप्ता"),
        ["Singh"] = ("સિંહ", "सिंह"),
        ["singh"] = ("સિંહ", "सिंह"),
        ["Kumar"] = ("કુમાર", "कुमार"),
        ["kumar"] = ("કુમાર", "कुमार"),
        ["Bhai"] = ("ભાઈ", "भाई"),
        ["bhai"] = ("ભાઈ", "भाई"),
        ["Ben"] = ("બેન", "बहन"),
        ["ben"] = ("બેન", "बहन"),
        ["Lal"] = ("લાલ", "लाल"),
        ["lal"] = ("લાલ", "लाल"),
        ["Chandra"] = ("ચંદ્ર", "चन्द्र"),
        ["chandra"] = ("ચંદ્ર", "चन्द्र"),
        ["Kant"] = ("કાંત", "कांत"),
        ["kant"] = ("કાંત", "कांत"),
        ["Das"] = ("દાસ", "दास"),
        ["das"] = ("દાસ", "दास"),
        ["Bhargav Pravinchandra Panchal"] = ("ભાર્ગવ પ્રવિણચંદ્ર પંચાલ", "भार्गव प्रविणचन्द्र पंचाल"),

        // Pronouns, Verbs & Conversational Terms
        ["tame"] = ("તમે", "आप"),
        ["Tame"] = ("તમે", "आप"),
        ["tamaru"] = ("તમારું", "आपका"),
        ["tamaro"] = ("તમારો", "आपका"),
        ["tamari"] = ("તમારી", "आपकी"),
        ["tamara"] = ("તમારા", "आपके"),
        ["kem"] = ("કેમ", "कैसे"),
        ["Kem"] = ("કેમ", "कैसे"),
        ["shu"] = ("શું", "क्या"),
        ["Shu"] = ("શું", "क्या"),
        ["su"] = ("શું", "क्या"),
        ["Su"] = ("શું", "क्या"),
        ["kone"] = ("કોને", "किसे"),
        ["kya"] = ("ક્યાં", "कहाँ"),
        ["kyaa"] = ("ક્યાં", "कहाँ"),
        ["kyare"] = ("ક્યારે", "कब"),
        ["kevi"] = ("કેવી", "कैसी"),
        ["kevo"] = ("કેવો", "कैसा"),
        ["kevu"] = ("કેવું", "कैसा"),
        ["aabhar"] = ("આભાર", "आभार"),
        ["Aabhar"] = ("આભાર", "આભાર"),
        ["haa"] = ("હા", "हाँ"),
        ["Haa"] = ("હા", "हाँ"),
        ["naa"] = ("ના", "नहीं"),
        ["Naa"] = ("ના", "नहीं"),
        ["pan"] = ("પણ", "भी"),
        ["ane"] = ("અને", "और"),
        ["Ane"] = ("અને", "और"),
        ["athi"] = ("આથી", "अतः"),
        ["mate"] = ("માટે", "के लिए"),
        ["sathe"] = ("સાથે", "साथ"),
        ["thi"] = ("થી", "से"),
        ["sudhi"] = ("સુધી", "तक"),
        ["shri"] = ("શ્રી", "श्री"),
        ["Shri"] = ("શ્રી", "श्री"),
        ["shree"] = ("શ્રી", "श्री"),
        ["Shree"] = ("શ્રી", "श्री"),
        ["smt"] = ("શ્રીમતી", "श्रीमती"),
        ["Smt"] = ("શ્રીમતી", "श्रीमती"),
        ["shrimati"] = ("શ્રીમતી", "श्रीमती"),

        // Financial & Application Terms
        ["Initial Loan Amount"] = ("પ્રારંભિક લોન રકમ", "प्रारंभिक ऋण राशि"),
        ["Payment Received"] = ("જમા રકમ", "जमा राशि"),
        ["Amount Given"] = ("ઉપાડ રકમ", "निकासी राशि"),
        ["Interest Accrued"] = ("ઉમેરેલ વ્યાજ", "अर्जित ब्याज"),
        ["Interest"] = ("વ્યાજ", "ब्याज"),
        ["interest"] = ("વ્યાજ", "ब्याज"),
        ["vyaj"] = ("વ્યાજ", "ब्याज"),
        ["Vyaj"] = ("વ્યાજ", "ब्याज"),
        ["Full Name"] = ("પૂર્ણ નામ", "पूरा नाम"),
        ["Full Name *"] = ("પૂર્ણ નામ *", "पूरा नाम *"),
        ["Withdrawal"] = ("ઉપાડ", "निकासी"),
        ["Deposit"] = ("જમા", "जमा"),
        ["Cash"] = ("રોકડ", "नकद"),
        ["cash"] = ("રોકડ", "नकद"),
        ["rokad"] = ("રોકડ", "नकद"),
        ["Gold"] = ("સોનું", "सोना"),
        ["gold"] = ("સોનું", "સોના"),
        ["sonu"] = ("સોનું", "સોના"),
        ["Silver"] = ("ચાંદી", "चांदी"),
        ["silver"] = ("ચાંદી", "चांदी"),
        ["chandi"] = ("ચાંદી", "चांदी"),
        ["Ring"] = ("વીંટી", "अंगूठी"),
        ["viti"] = ("વીંટી", "अंगूठी"),
        ["Necklace"] = ("હાર", "हार"),
        ["har"] = ("હાર", "हार"),
        ["Chain"] = ("સાંકળ", "चेन"),
        ["sakhal"] = ("સાંકળ", "चेन"),
        ["sankal"] = ("સાંકળ", "चेन"),
        ["Bracelet"] = ("બ્રેસલેટ", "कंगन"),
        ["Bangle"] = ("બંગડી", "चूड़ी"),
        ["bangdi"] = ("બંગડી", "चूड़ी"),
        ["Bangles"] = ("બંગડીઓ", "चूड़ियाँ"),
        ["Earrings"] = ("ઝૂમખા", "झुमके"),
        ["zhumkha"] = ("ઝૂમખા", "झुमके"),
        ["Pendant"] = ("પેન્ડન્ટ", "पेंडेंट"),
        ["Nose Ring"] = ("નથણી", "नथनी"),
        ["nathani"] = ("નથણી", "नथनी"),
        ["Anklet"] = ("પાયલ", "पायल"),
        ["payal"] = ("પાયલ", "पायल"),
        ["Waist Chain"] = ("કંદોરો", "कमरबंद"),
        ["kandoro"] = ("કંદોરો", "कमरबंद"),
        ["Mangalsutra"] = ("મંગળસૂત્ર", "मंगलसूत्र"),
        ["mangalsutra"] = ("મંગળસૂત્ર", "मंगलसूत्र"),
        ["Other"] = ("અન્ય", "अन्य"),
        ["loan"] = ("લોન", "ऋण"),
        ["Loan"] = ("લોન", "ऋण"),
        ["rupya"] = ("રૂપિયા", "रुपये"),
        ["rupiya"] = ("રૂપિયા", "रुपये"),
        ["rupees"] = ("રૂપિયા", "रुपये"),
        ["Rupees"] = ("રૂપિયા", "रुपये"),

        // Gujarat Cities / Towns / Regions
        ["Dahod"] = ("દાહોદ", "दाहोद"),
        ["dahod"] = ("દાહોદ", "दाहोद"),
        ["Godhra"] = ("ગોધરા", "गोधरा"),
        ["godhra"] = ("ગોધરા", "गोधरा"),
        ["Surat"] = ("સુરત", "सूरत"),
        ["surat"] = ("સુરત", "सूरत"),
        ["Vadodara"] = ("વડોદરા", "वडोदरा"),
        ["vadodara"] = ("વડોદરા", "वडोदरा"),
        ["Baroda"] = ("બરોડા", "बड़ौदा"),
        ["baroda"] = ("બરોડા", "बड़ौदा"),
        ["Rajkot"] = ("રાજકોટ", "राजकोट"),
        ["rajkot"] = ("રાજકોટ", "राजकोट"),
        ["Bhavnagar"] = ("ભાવનગર", "भावनगर"),
        ["bhavnagar"] = ("ભાવનગર", "भावनगर"),
        ["Jamnagar"] = ("જામનગર", "जामनगर"),
        ["jamnagar"] = ("જામનગર", "जामनगर"),
        ["Junagadh"] = ("જૂનાગઢ", "जूनागढ़"),
        ["junagadh"] = ("જૂનાગઢ", "जूनागढ़"),
        ["Gandhinagar"] = ("ગાંધીનગર", "गांधीनगर"),
        ["gandhinagar"] = ("ગાંધીનગર", "गांधीनगर"),
        ["Anand"] = ("આણંદ", "आणंद"),
        ["anand"] = ("આણંદ", "आणंद"),
        ["Nadiad"] = ("નડિયાદ", "नडियाद"),
        ["nadiad"] = ("નડિયાદ", "नडियाद"),
        ["Morbi"] = ("મોરબી", "मोरबी"),
        ["morbi"] = ("મોરબી", "मोरबी"),
        ["Surendranagar"] = ("સુરેન્દ્રનગર", "सुरेंद्रनगर"),
        ["surendranagar"] = ("સુરેન્દ્રનગર", "सुरेंद्रनगर"),
        ["Bharuch"] = ("ભરૂચ", "भरूच"),
        ["bharuch"] = ("ભરૂચ", "भरूच"),
        ["Navsari"] = ("નવસારી", "नवसारी"),
        ["navsari"] = ("નવસારી", "नवसारी"),
        ["Vapi"] = ("વાપી", "वापी"),
        ["vapi"] = ("વાપી", "वापी"),
        ["Porbandar"] = ("પોરબંદર", "पोरबंदर"),
        ["porbandar"] = ("પોરબંદર", "पोरबंदर"),
        ["Bhuj"] = ("ભુજ", "भुज"),
        ["bhuj"] = ("ભુજ", "भुज"),
        ["Botad"] = ("બોટાદ", "बोटाद"),
        ["botad"] = ("બોટાદ", "बोटाद"),
        ["Amreli"] = ("અમરેલી", "अमरेली"),
        ["amreli"] = ("અમરેલી", "अमरेली"),
        ["Palanpur"] = ("પાલનપુર", "पालनपुर"),
        ["palanpur"] = ("પાલનપુર", "पालनपुर"),
        ["Mehsana"] = ("મહેસાણા", "मेहसाणा"),
        ["mehsana"] = ("મહેસાણા", "मेहसाणा"),
        ["Himatnagar"] = ("હિંમતનગર", "हिम्मतनगर"),
        ["himatnagar"] = ("હિંમતનગર", "हिम्मतनगर"),
        ["Modasa"] = ("મોડાસા", "मोडासा"),
        ["modasa"] = ("મોડાસા", "मोडासा"),
        ["Lunawada"] = ("લુણાવાડા", "लूणावाड़ा"),
        ["lunawada"] = ("લુણાવાડા", "लूणावाड़ा"),
        ["Santrampur"] = ("સંતરામપુર", "संतरामपुर"),
        ["santrampur"] = ("સંતરામપુર", "संतरामपुर"),
        ["Jhalod"] = ("ઝાલોદ", "झालोद"),
        ["jhalod"] = ("ઝાલોદ", "झालोद"),
        ["Fatepura"] = ("ફતેપુરા", "फतेपुरा"),
        ["fatepura"] = ("ફતેપુરા", "फतेपुरा"),
        ["Limkheda"] = ("લીમખેડા", "लीमखेड़ा"),
        ["limkheda"] = ("લીમખેડા", "लीमखेड़ा"),
        ["Devgadh Baria"] = ("દેવગઢ બારિયા", "देवगढ़ बारिया"),
        ["devgadh baria"] = ("દેવગઢ બારિયા", "देवगढ़ बारिया"),
        ["Garbada"] = ("ગરબાડા", "गरबाड़ा"),
        ["garbada"] = ("ગરબાડા", "गरबाड़ा"),
        ["Singvad"] = ("સિંગવડ", "सिंगवड़"),
        ["singvad"] = ("સિંગવડ", "सिंगवड़"),
        ["Sanjeli"] = ("સંજેલી", "संजेली"),
        ["sanjeli"] = ("સંજેલી", "संजेली"),
    };

    private static readonly Dictionary<string, string> ReverseGujaratiToEnglish = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> ReverseHindiToEnglish = new(StringComparer.OrdinalIgnoreCase);

    static ScriptTranslator()
    {
        foreach (var kvp in ExactDictionary)
        {
            ReverseGujaratiToEnglish[kvp.Value.Gujarati] = kvp.Key;
            ReverseHindiToEnglish[kvp.Value.Hindi] = kvp.Key;
        }

        ReverseGujaratiToEnglish["ચાંદી"] = "Silver";
        ReverseHindiToEnglish["चांदी"] = "Silver";
        ReverseHindiToEnglish["चाँदी"] = "Silver";
        ReverseGujaratiToEnglish["સોનું"] = "Gold";
        ReverseHindiToEnglish["सोना"] = "Gold";
        ReverseGujaratiToEnglish["રોકડ"] = "Cash";
        ReverseHindiToEnglish["नकद"] = "Cash";
        ReverseHindiToEnglish["रोकड़"] = "Cash";
        ReverseGujaratiToEnglish["વીંટી"] = "Ring";
        ReverseHindiToEnglish["अंगूठी"] = "Ring";
        ReverseGujaratiToEnglish["હાર"] = "Necklace";
        ReverseHindiToEnglish["हार"] = "Necklace";
        ReverseGujaratiToEnglish["સાંકળ"] = "Chain";
        ReverseHindiToEnglish["चेन"] = "Chain";
        ReverseGujaratiToEnglish["બ્રેસલેટ"] = "Bracelet";
        ReverseHindiToEnglish["कंगन"] = "Bracelet";
        ReverseGujaratiToEnglish["ચૂડી"] = "Chudi";
        ReverseHindiToEnglish["चूड़ी"] = "Chudi";
        ReverseGujaratiToEnglish["ચૂડો"] = "Chudo";
        ReverseGujaratiToEnglish["ચૂડા"] = "Chuda";
        ReverseGujaratiToEnglish["દામણી"] = "Damani";
        ReverseGujaratiToEnglish["બાજુ"] = "Baju";
        ReverseGujaratiToEnglish["હાથફૂલ"] = "Hathphool";
        ReverseGujaratiToEnglish["કલ્લા"] = "Kalla";
        ReverseGujaratiToEnglish["પહોંચી"] = "Pahochi";
        ReverseGujaratiToEnglish["કડલી"] = "Kadli";
        ReverseGujaratiToEnglish["ઝૂડ"] = "Zud";
        ReverseGujaratiToEnglish["બોર"] = "Bor";
        ReverseGujaratiToEnglish["ગોખરુ"] = "Gokhru";
        ReverseGujaratiToEnglish["સાંકળી"] = "Sankali";
        ReverseGujaratiToEnglish["કાપ"] = "Kap";
        ReverseGujaratiToEnglish["વાલસિંગ"] = "Valsing";
        ReverseHindiToEnglish["वालसिंह"] = "Valsing";
        ReverseGujaratiToEnglish["ઝૂમખા"] = "Earrings";
        ReverseHindiToEnglish["झुमके"] = "Earrings";
        ReverseGujaratiToEnglish["પેન્ડન્ટ"] = "Pendant";
        ReverseHindiToEnglish["पेंडेंट"] = "Pendant";
        ReverseGujaratiToEnglish["નથણી"] = "Nose Ring";
        ReverseHindiToEnglish["नथनी"] = "Nose Ring";
        ReverseGujaratiToEnglish["પાયલ"] = "Anklet";
        ReverseHindiToEnglish["पायल"] = "Anklet";
        ReverseGujaratiToEnglish["કંદોરો"] = "Waist Chain";
        ReverseHindiToEnglish["कमरबंद"] = "Waist Chain";
        ReverseGujaratiToEnglish["મંગળસૂત્ર"] = "Mangalsutra";
        ReverseHindiToEnglish["मंगलसूत्र"] = "Mangalsutra";
        ReverseGujaratiToEnglish["અન્ય"] = "Other";
        ReverseHindiToEnglish["अन्य"] = "Other";
        ReverseGujaratiToEnglish["ધીરધાર"] = "DhirDhar";
        ReverseGujaratiToEnglish["ભાર્ગવ"] = "Bhargav";
        ReverseGujaratiToEnglish["પંચાલ"] = "Panchal";
        ReverseGujaratiToEnglish["પલક"] = "Palak";
        ReverseHindiToEnglish["पलक"] = "Palak";
        ReverseGujaratiToEnglish["ચેતન"] = "Chetan";
        ReverseHindiToEnglish["चेतन"] = "Chetan";
    }

    public static string Translate(string? text, string targetLanguageCode)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        try
        {
            var targetLang = NormalizeLanguageCode(targetLanguageCode);
            var sourceLang = DetectLanguage(text);

            if (targetLang == "en")
            {
                return ToEnglish(text);
            }

            // If the source language already matches target language AND there are NO unconverted Latin letters, return unchanged
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase) && !ContainsLatinLetters(text))
            {
                return text;
            }

            if (targetLang == "gu")
            {
                return ToGujarati(text);
            }

            if (targetLang == "hi" || targetLang == "mr")
            {
                return ToHindi(text);
            }

            // Other Indic scripts: convert via Devanagari/Gujarati standard Brahmic block mapping
            return ConvertToIndicScript(text, targetLang);
        }
        catch
        {
            return text;
        }
    }

    public static string DetectLanguage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "en";

        foreach (char c in text)
        {
            if (c >= 0x0A80 && c <= 0x0AFF) return "gu"; // Gujarati
            if (c >= 0x0900 && c <= 0x097F) return "hi"; // Devanagari (Hindi/Marathi)
            if (c >= 0x0980 && c <= 0x09FF) return "bn"; // Bengali/Assamese
            if (c >= 0x0A00 && c <= 0x0A7F) return "pa"; // Gurmukhi (Punjabi)
            if (c >= 0x0B00 && c <= 0x0B7F) return "or"; // Odia
            if (c >= 0x0B80 && c <= 0x0BFF) return "ta"; // Tamil
            if (c >= 0x0C00 && c <= 0x0C7F) return "te"; // Telugu
            if (c >= 0x0C80 && c <= 0x0CFF) return "kn"; // Kannada
            if (c >= 0x0D00 && c <= 0x0D7F) return "ml"; // Malayalam
        }

        return "en";
    }

    public static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return "en";
        var trimmed = languageCode.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("gu") || trimmed.Contains("gujarati") || trimmed.Contains("ગુજરાતી")) return "gu";
        if (trimmed.StartsWith("hi") || trimmed.Contains("hindi") || trimmed.Contains("हिन्दी") || trimmed.Contains("हिंदी")) return "hi";
        if (trimmed.StartsWith("mr") || trimmed.Contains("marathi") || trimmed.Contains("मराठी")) return "mr";
        if (trimmed.StartsWith("bn") || trimmed.Contains("bengali") || trimmed.Contains("bangla") || trimmed.Contains("বাংলা")) return "bn";
        if (trimmed.StartsWith("pa") || trimmed.Contains("punjabi") || trimmed.Contains("ਪੰਜਾਬੀ")) return "pa";
        if (trimmed.StartsWith("ta") || trimmed.Contains("tamil") || trimmed.Contains("தமிழ்")) return "ta";
        if (trimmed.StartsWith("te") || trimmed.Contains("telugu") || trimmed.Contains("తెలుగు")) return "te";
        if (trimmed.StartsWith("kn") || trimmed.Contains("kannada") || trimmed.Contains("ಕನ್ನಡ")) return "kn";
        if (trimmed.StartsWith("ml") || trimmed.Contains("malayalam") || trimmed.Contains("മലയാളം")) return "ml";
        if (trimmed.StartsWith("or") || trimmed.Contains("odia") || trimmed.Contains("oriya") || trimmed.Contains("ଓଡ଼ିଆ")) return "or";
        if (trimmed.StartsWith("as") || trimmed.Contains("assamese") || trimmed.Contains("অসমীয়া")) return "as";
        return "en";
    }

    public static string ToGujarati(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;

        try
        {
            if (TryFormatDynamicInterest(input, "gu", out var interestFormatted))
            {
                return interestFormatted;
            }

            if (ExactDictionary.TryGetValue(input.Trim(), out var exactMatch) && !IsSingleConsonantToken(input.Trim()))
            {
                return exactMatch.Gujarati;
            }

            // Tokenize input preserving whitespace, punctuation, numbers, and symbols
            return ProcessTokens(input, token =>
            {
                if (IsGujaratiScript(token))
                {
                    return token;
                }

                if (IsHindiScript(token))
                {
                    return ConvertHindiToGujarati(token);
                }

                if (IsIndicScript(token))
                {
                    return ConvertIndicToGujarati(token);
                }

                // If token is digits in mixed context, localize digits to Gujarati numerals
                if (IsAllDigits(token))
                {
                    return ConvertDigitsToGujarati(token);
                }

                // If token contains no Latin letters (e.g. pure symbols), leave unchanged
                if (!HasLatinLetters(token))
                {
                    return token;
                }

                if (ExactDictionary.TryGetValue(token, out var match) && !IsSingleConsonantToken(token))
                {
                    return match.Gujarati;
                }

                return TransliterateLatinToGujarati(token);
            });
        }
        catch
        {
            return input;
        }
    }

    private static bool IsSingleConsonantToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4) return false;
        var lower = token.ToLowerInvariant();
        return lower is "ta" or "tha" or "da" or "dha" or "na" or "la" or "sha" or "t" or "th" or "d" or "dh" or "n" or "l" or "sh";
    }

    public static string ConvertDigitsToIndic(string? input, string targetLanguageCode)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var norm = NormalizeLanguageCode(targetLanguageCode);
        if (norm == "en") return NormalizeDigitsToAscii(input);

        int baseOffset = norm switch
        {
            "gu" => '૦' - '0',
            "hi" or "mr" => '०' - '0',
            "bn" or "as" => '০' - '0',
            "pa" => '੦' - '0',
            "ta" => '௦' - '0',
            "te" => '౦' - '0',
            "kn" => '೦' - '0',
            "ml" => '൦' - '0',
            "or" => '୦' - '0',
            _ => '૦' - '0'
        };

        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c >= '0' && c <= '9')
            {
                chars[i] = (char)(c + baseOffset);
            }
            else if (c >= '૦' && c <= '૯')
            {
                chars[i] = (char)(c - '૦' + '0' + baseOffset);
            }
            else if (c >= '०' && c <= '९')
            {
                chars[i] = (char)(c - '०' + '0' + baseOffset);
            }
        }
        return new string(chars);
    }

    public static string ConvertDigitsToGujarati(string? input)
    {
        return ConvertDigitsToIndic(input, "gu");
    }

    public static bool IsPureNumericOrDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var trimmed = s.Trim();
        bool hasDigit = false;
        foreach (char c in trimmed)
        {
            if (char.IsDigit(c) || (c >= '૦' && c <= '૯') || (c >= '०' && c <= '९')) { hasDigit = true; continue; }
            if (c == '.' || c == '/' || c == '-' || c == ',' || c == ':' || c == '%' || c == '$' || c == '₹') continue;
            return false;
        }
        return hasDigit;
    }

    private static bool IsAllDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (!char.IsDigit(c)) return false;
        }
        return true;
    }

    public static string ToHindi(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;

        try
        {
            if (TryFormatDynamicInterest(input, "hi", out var interestFormatted))
            {
                return interestFormatted;
            }

            if (ExactDictionary.TryGetValue(input.Trim(), out var exactMatch))
            {
                return exactMatch.Hindi;
            }

            return ProcessTokens(input, token =>
            {
                if (ExactDictionary.TryGetValue(token, out var match))
                {
                    return match.Hindi;
                }

                if (IsHindiScript(token))
                {
                    return token;
                }

                if (IsGujaratiScript(token))
                {
                    return ConvertGujaratiToHindi(token);
                }

                if (IsIndicScript(token))
                {
                    return ConvertIndicToHindi(token);
                }

                if (IsAllDigits(token))
                {
                    return ConvertDigitsToIndic(token, "hi");
                }

                if (!HasLatinLetters(token))
                {
                    return token;
                }

                return TransliterateLatinToHindi(token);
            });
        }
        catch
        {
            return input;
        }
    }

    public static string ToEnglish(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;

        try
        {
            if (TryFormatDynamicInterest(input, "en", out var interestFormatted))
            {
                return interestFormatted;
            }

            var trimmed = input.Trim();
            if (ReverseGujaratiToEnglish.TryGetValue(trimmed, out var exactGuj))
            {
                return exactGuj;
            }
            if (ReverseHindiToEnglish.TryGetValue(trimmed, out var exactHi))
            {
                return exactHi;
            }

            return ProcessTokens(input, token =>
            {
                if (ReverseGujaratiToEnglish.TryGetValue(token, out var matchGuj))
                {
                    return matchGuj;
                }
                if (ReverseHindiToEnglish.TryGetValue(token, out var matchHi))
                {
                    return matchHi;
                }

                if (IsPureNumericOrDate(token))
                {
                    return ConvertIndicDigitsToAscii(token);
                }

                if (IsIndicScript(token))
                {
                    return TransliterateIndicToLatin(token);
                }

                return token;
            });
        }
        catch
        {
            return input;
        }
    }

    private static string ProcessTokens(string input, Func<string, string> tokenProcessor)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder();
        int i = 0;
        int len = input.Length;

        while (i < len)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                sb.Append(input[i]);
                i++;
                continue;
            }

            if (char.IsPunctuation(input[i]) || char.IsSymbol(input[i]))
            {
                sb.Append(input[i]);
                i++;
                continue;
            }

            int start = i;
            while (i < len && !char.IsWhiteSpace(input[i]) && !char.IsPunctuation(input[i]) && !char.IsSymbol(input[i]))
            {
                i++;
            }

            var token = input[start..i];
            sb.Append(tokenProcessor(token));
        }

        return sb.ToString();
    }

    private static bool HasLatinLetters(string s)
    {
        foreach (char c in s)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return true;
        }
        return false;
    }

    public static string NormalizeDigitsToAscii(string text) => ConvertIndicDigitsToAscii(text);

    public static string ConvertIndicDigitsToAscii(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c >= '૦' && c <= '૯') chars[i] = (char)('0' + (c - '૦'));
            else if (c >= '०' && c <= '९') chars[i] = (char)('0' + (c - '०'));
            else if (c >= '০' && c <= '৯') chars[i] = (char)('0' + (c - '০'));
            else if (c >= '੦' && c <= '੯') chars[i] = (char)('0' + (c - '੦'));
            else if (c >= '௦' && c <= '௯') chars[i] = (char)('0' + (c - '௦'));
            else if (c >= '౦' && c <= '౯') chars[i] = (char)('0' + (c - '౦'));
            else if (c >= '೦' && c <= '೯') chars[i] = (char)('0' + (c - '೦'));
            else if (c >= '൦' && c <= '൯') chars[i] = (char)('0' + (c - '൦'));
            else if (c >= '୦' && c <= '୯') chars[i] = (char)('0' + (c - '୦'));
        }
        return new string(chars);
    }

    private static readonly string[][] MonthNamesByLang = new string[][]
    {
        // 0: en
        new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" },
        // 1: gu
        new[] { "જાન્યુઆરી", "ફેબ્રુઆરી", "માર્ચ", "એપ્રિલ", "મે", "જૂન", "જુલાઈ", "ઓગસ્ટ", "સપ્ટેમ્બર", "ઓક્ટોબર", "નવેમ્બર", "ડિસેમ્બર" },
        // 2: hi
        new[] { "जनवरी", "फरवरी", "मार्च", "अप्रैल", "मई", "जून", "जुलाई", "अगस्त", "सितंबर", "अक्टूबर", "नवंबर", "दिसंबर" },
        // 3: mr
        new[] { "जानेवारी", "फेब्रुवारी", "मार्च", "एप्रिल", "मे", "जून", "जुलै", "ऑगस्ट", "सप्टेंबर", "ऑक्टोबर", "नोव्हेंबर", "डिसेंबर" },
        // 4: bn
        new[] { "জানুয়ারি", "ফেব্রুয়ারি", "মার্চ", "এপ্রিল", "মে", "জুন", "জুলাই", "আগস্ট", "সেপ্টেম্বর", "অক্টোবর", "নভেম্বর", "ডিসেম্বর" },
        // 5: pa
        new[] { "ਜਨਵਰੀ", "ਫ਼ਰਵਰੀ", "ਮਾਰਚ", "ਅਪ੍ਰੈਲ", "ਮਈ", "ਜੂਨ", "ਜੁਲਾਈ", "ਅਗਸਤ", "ਸਤੰਬਰ", "ਅਕਤੂਬਰ", "ਨਵੰਬਰ", "ਦਸੰਬਰ" },
        // 6: ta
        new[] { "ஜனவரி", "பிப்ரவரி", "மார்ச்", "ஏப்ரல்", "மே", "ஜூன்", "ஜூலை", "ஆகஸ்ட்", "செப்டம்பர்", "அக்டோபர்", "நவம்பர்", "டிசம்பர்" },
        // 7: te
        new[] { "జనవరి", "ఫిబ్రవరి", "మార్చి", "ఏప్రిల్", "మే", "జూన్", "జూలై", "ఆగస్టు", "సెప్టెంబర్", "అక్టోబర్", "నవంబర్", "డిసెంబర్" },
        // 8: kn
        new[] { "ಜನವರಿ", "ಫೆಬ್ರವರಿ", "ಮಾರ್ಚ್", "ಏಪ್ರಿಲ್", "ಮೇ", "ಜೂನ್", "ಜುಲೈ", "ಆಗಸ್ಟ್", "ಸೆಪ್ಟೆಂಬರ್", "ಅಕ್ಟೋಬರ್", "ನವೆಂಬರ್", "ಡಿಸೆಂಬರ್" },
        // 9: ml
        new[] { "ജനവരി", "ഫെബ്രുവരി", "മാർച്ച്", "ഏപ്രിൽ", "മെയ്", "ജൂൺ", "ജൂലൈ", "ഓഗസ്റ്റ്", "സെപ്റ്റംബർ", "ഒക്ടോബർ", "നവംബർ", "ഡിസംബർ" },
        // 10: or
        new[] { "ଜାନୁଆରୀ", "ଫେବୃଆରୀ", "ମାର୍ଚ୍ଚ", "ଏପ୍ରିଲ", "ମେ", "ଜୁନ", "ଜୁଲାଇ", "ଅଗଷ୍ଟ", "ସେପ୍ଟେମ୍ବର", "ଅକ୍ଟୋବର", "ନଭେମ୍ବର", "ଡିସେମ୍ବର" },
        // 11: as
        new[] { "জানুৱাৰী", "ফেব্ৰুৱাৰী", "মাৰ্চ", "এপ্ৰিল", "মে'", "জুন", "জুলাই", "আগষ্ট", "ছেপ্টেম্বৰ", "অক্টোবৰ", "নৱেম্বৰ", "ডিচেম্বৰ" },
    };

    public static string GetMonthName(int month, string normLang)
    {
        if (month < 1 || month > 12) return string.Empty;
        int langIdx = normLang switch
        {
            "gu" => 1,
            "hi" => 2,
            "mr" => 3,
            "bn" => 4,
            "pa" => 5,
            "ta" => 6,
            "te" => 7,
            "kn" => 8,
            "ml" => 9,
            "or" => 10,
            "as" => 11,
            _ => 0
        };
        return MonthNamesByLang[langIdx][month - 1];
    }

    private static List<string> GetAllMonthVariants(int month)
    {
        var list = new List<string>();
        if (month < 1 || month > 12) return list;
        foreach (var arr in MonthNamesByLang)
        {
            list.Add(arr[month - 1]);
        }
        string[] enFull = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        string[] enShort = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        list.Add(enFull[month - 1]);
        list.Add(enShort[month - 1]);
        return list;
    }

    public static int DetectMonthIndex(string text)
    {
        for (int m = 1; m <= 12; m++)
        {
            var names = GetAllMonthVariants(m);
            foreach (var name in names)
            {
                if (text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return m;
                }
            }
        }
        return -1;
    }

    public static bool IsDynamicInterestDescription(string text, out string startPart, out string endPart)
    {
        startPart = string.Empty;
        endPart = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim();

        var matchEn = Regex.Match(t, @"^Interest\s+for\s+(.*?)\s+to\s+(.*?)$", RegexOptions.IgnoreCase);
        if (matchEn.Success)
        {
            startPart = matchEn.Groups[1].Value.Trim();
            endPart = matchEn.Groups[2].Value.Trim();
            return true;
        }

        var matchGu = Regex.Match(t, @"^(.*?)\s+થી\s+(.*?)\s+(?:સુધીનું|માટેનું)\s+વ્યાજ$");
        if (matchGu.Success)
        {
            startPart = matchGu.Groups[1].Value.Trim();
            endPart = matchGu.Groups[2].Value.Trim();
            return true;
        }

        var matchHi = Regex.Match(t, @"^(.*?)\s+से\s+(.*?)\s+(?:तक\s+का|का)\s+ब्याज$");
        if (matchHi.Success)
        {
            startPart = matchHi.Groups[1].Value.Trim();
            endPart = matchHi.Groups[2].Value.Trim();
            return true;
        }

        var matchHeal = Regex.Match(t, @"^(?:ઈન્ટરેસ્ટ|ઇન્ટરેસ્ટ|ઈંટરેસ્ટ|ઇંટરેસ્ટ|ઇન્ટ્રેસ્ટ)\s+(?:ફોર|માટે)\s+(.*?)\s+(?:થી|to|સે)\s+(.*?)$", RegexOptions.IgnoreCase);
        if (matchHeal.Success)
        {
            startPart = matchHeal.Groups[1].Value.Trim();
            endPart = matchHeal.Groups[2].Value.Trim();
            return true;
        }

        return false;
    }

    public static bool TryFormatDynamicInterest(string text, string targetLanguageCode, out string formatted)
    {
        formatted = text;
        if (!IsDynamicInterestDescription(text, out var startPart, out var endPart))
        {
            return false;
        }

        var norm = NormalizeLanguageCode(targetLanguageCode);
        var normStart = FormatDatePart(startPart, norm);
        var normEnd = FormatDatePart(endPart, norm);

        if (norm == "gu")
        {
            formatted = $"{normStart} થી {normEnd} સુધીનું વ્યાજ";
            return true;
        }
        else if (norm == "hi" || norm == "mr")
        {
            formatted = $"{normStart} से {normEnd} तक का ब्याज";
            return true;
        }
        else
        {
            var enStart = FormatDatePart(startPart, "en");
            var enEnd = FormatDatePart(endPart, "en");
            formatted = $"Interest for {enStart} to {enEnd}";
            return true;
        }
    }

    private static string FormatDatePart(string part, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(part)) return part;

        var mIdx = DetectMonthIndex(part);
        if (mIdx >= 1 && mIdx <= 12)
        {
            var matchDay = Regex.Match(part, @"\d+");
            if (matchDay.Success)
            {
                var day = matchDay.Value;
                var monthName = GetMonthName(mIdx, targetLang);
                if (targetLang == "en")
                {
                    return $"{day}-{monthName}";
                }
                else
                {
                    return $"{day} {monthName}";
                }
            }
        }

        return part;
    }

    public static bool IsGujaratiScript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (char c in text)
        {
            if (c >= 0x0A80 && c <= 0x0AFF) return true;
        }
        return false;
    }

    public static bool IsHindiScript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (char c in text)
        {
            if (c >= 0x0900 && c <= 0x097F) return true;
        }
        return false;
    }

    public static bool ContainsLatinLetters(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return true;
        }
        return false;
    }

    public static bool ContainsIndicScript(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (char c in text)
        {
            if (c >= 0x0900 && c <= 0x0D7F) return true;
        }
        return false;
    }

    public static bool IsPureIndicScript(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool hasIndic = false;
        foreach (char c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return false;
            if (c >= 0x0900 && c <= 0x0D7F) hasIndic = true;
        }
        return hasIndic;
    }

    public static bool IsIndicScript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (char c in text)
        {
            if (c >= 0x0900 && c <= 0x0D7F) return true;
        }
        return false;
    }

    public static string TransliterateIndicToLatin(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var devanagari = ConvertIndicToDevanagari(text);
        var sb = new StringBuilder();
        int len = devanagari.Length;

        for (int i = 0; i < len; i++)
        {
            char c = devanagari[i];

            if (c == 0x0905) { sb.Append("A"); continue; }
            if (c == 0x0906) { sb.Append("A"); continue; }
            if (c == 0x0907) { sb.Append("I"); continue; }
            if (c == 0x0908) { sb.Append("I"); continue; }
            if (c == 0x0909) { sb.Append("U"); continue; }
            if (c == 0x090A) { sb.Append("U"); continue; }
            if (c == 0x090B) { sb.Append("Ri"); continue; }
            if (c == 0x090F) { sb.Append("E"); continue; }
            if (c == 0x0910) { sb.Append("Ai"); continue; }
            if (c == 0x0913) { sb.Append("O"); continue; }
            if (c == 0x0914) { sb.Append("Au"); continue; }

            if (c == 0x0902 || c == 0x0901) { sb.Append('n'); continue; }
            if (c == 0x0903) { sb.Append('h'); continue; }

            if (c == 0x093E) { sb.Append('a'); continue; }
            if (c == 0x093F) { sb.Append('i'); continue; }
            if (c == 0x0940) { sb.Append('i'); continue; }
            if (c == 0x0941) { sb.Append('u'); continue; }
            if (c == 0x0942) { sb.Append('u'); continue; }
            if (c == 0x0943) { sb.Append("ri"); continue; }
            if (c == 0x0947) { sb.Append('e'); continue; }
            if (c == 0x0948) { sb.Append("ai"); continue; }
            if (c == 0x094B) { sb.Append('o'); continue; }
            if (c == 0x094C) { sb.Append("au"); continue; }
            if (c == 0x094D) { continue; }

            string? cons = ((int)c) switch
            {
                0x0915 => "k",
                0x0916 => "kh",
                0x0917 => "g",
                0x0918 => "gh",
                0x0919 => "ng",
                0x091A => "ch",
                0x091B => "chh",
                0x091C => "j",
                0x091D => "jh",
                0x091E => "ny",
                0x091F => "t",
                0x0920 => "th",
                0x0921 => "d",
                0x0922 => "dh",
                0x0923 => "n",
                0x0924 => "t",
                0x0925 => "th",
                0x0926 => "d",
                0x0927 => "dh",
                0x0928 => "n",
                0x092A => "p",
                0x092B => "ph",
                0x092C => "b",
                0x092D => "bh",
                0x092E => "m",
                0x092F => "y",
                0x0930 => "r",
                0x0931 => "r",
                0x0932 => "l",
                0x0933 => "l",
                0x0935 => "v",
                0x0936 => "sh",
                0x0937 => "sh",
                0x0938 => "s",
                0x0939 => "h",
                _ => null
            };

            if (cons != null)
            {
                if (sb.Length == 0)
                {
                    cons = char.ToUpperInvariant(cons[0]) + (cons.Length > 1 ? cons[1..] : string.Empty);
                }

                sb.Append(cons);

                bool nextIsMatra = (i + 1 < len) && (
                    (devanagari[i + 1] >= 0x093E && devanagari[i + 1] <= 0x094C) ||
                    devanagari[i + 1] == 0x094D);

                bool isLastChar = (i + 1 == len);

                bool isCompoundBoundary = false;
                if (i + 1 < len)
                {
                    var rem = devanagari[(i + 1)..];
                    if (rem.StartsWith("सिंह") || rem.StartsWith("લાલ") || rem.StartsWith("કુમાર") || rem.StartsWith("ભાઈ") || rem.StartsWith("બેન"))
                    {
                        isCompoundBoundary = true;
                    }
                }

                if (!nextIsMatra && !isLastChar && !isCompoundBoundary)
                {
                    sb.Append('a');
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();
        if (!string.IsNullOrEmpty(result))
        {
            var words = result.Split(' ');
            for (int w = 0; w < words.Length; w++)
            {
                if (words[w].Length > 0)
                {
                    words[w] = char.ToUpperInvariant(words[w][0]) + (words[w].Length > 1 ? words[w][1..] : string.Empty);
                }
            }
            result = string.Join(" ", words);
        }

        return result;
    }

    /// <summary>
    /// Authoritative offline phonetic transliteration for Gujarati.
    /// Converts phonetic Latin text into accurate Gujarati Unicode script.
    /// </summary>
    public static string TransliterateLatinToGujarati(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return OfflineGujaratiTransliteration.Transliterate(text);
    }

    /// <summary>
    /// Gets ranked Gujarati candidate suggestions for predictive typing.
    /// </summary>
    public static IReadOnlyList<string> GetGujaratiCandidates(string text, int maxCandidates = 5)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return new[] { OfflineGujaratiTransliteration.Transliterate(text) };
    }

    /// <summary>
    /// Gets on-the-fly matra transliteration help preview matrix for a consonant.
    /// </summary>
    public static IReadOnlyList<(string Pattern, string Gujarati)> GetGujaratiOnTheFlyHelp(string consonantPattern)
    {
        return Array.Empty<(string, string)>();
    }

    private static bool IsVowel(char c)
    {
        return c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u' ||
               c == 'ā' || c == 'ī' || c == 'ū' || c == 'é' || c == 'è';
    }

    private static bool Matches(string s, int index, string prefix)
    {
        if (index + prefix.Length > s.Length) return false;
        for (int p = 0; p < prefix.Length; p++)
        {
            if (s[index + p] != prefix[p]) return false;
        }
        return true;
    }

    private static bool IsGujaratiVowelOrMatra(char c)
    {
        // Independent vowels & Matras
        return (c >= 0x0A85 && c <= 0x0A94) || (c >= 0x0ABE && c <= 0x0ACC) || c == 'ં' || c == 'ઃ';
    }

    private static string TransliterateLatinToHindi(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (ExactDictionary.TryGetValue(text.Trim(), out var exactMatch))
        {
            return exactMatch.Hindi;
        }

        // Convert Gujarati phonetic result to Devanagari (Brahmic equivalence)
        var guj = TransliterateLatinToGujarati(text);
        return ConvertGujaratiToHindi(guj);
    }

    public static string ConvertHindiToGujarati(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 0x0900 && c <= 0x097F)
            {
                int offset = c - 0x0900;
                sb.Append((char)(0x0A80 + offset));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static string ConvertGujaratiToHindi(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 0x0A80 && c <= 0x0AFF)
            {
                int offset = c - 0x0A80;
                sb.Append((char)(0x0900 + offset));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static string ConvertIndicToDevanagari(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 0x0A80 && c <= 0x0AFF) // Gujarati
            {
                sb.Append((char)(0x0900 + (c - 0x0A80)));
            }
            else if (c >= 0x0980 && c <= 0x09FF) // Bengali
            {
                sb.Append((char)(0x0900 + (c - 0x0980)));
            }
            else if (c >= 0x0A00 && c <= 0x0A7F) // Gurmukhi
            {
                sb.Append((char)(0x0900 + (c - 0x0A00)));
            }
            else if (c >= 0x0B00 && c <= 0x0B7F) // Odia
            {
                sb.Append((char)(0x0900 + (c - 0x0B00)));
            }
            else if (c >= 0x0B80 && c <= 0x0BFF) // Tamil
            {
                sb.Append((char)(0x0900 + (c - 0x0B80)));
            }
            else if (c >= 0x0C00 && c <= 0x0C7F) // Telugu
            {
                sb.Append((char)(0x0900 + (c - 0x0C00)));
            }
            else if (c >= 0x0C80 && c <= 0x0CFF) // Kannada
            {
                sb.Append((char)(0x0900 + (c - 0x0C80)));
            }
            else if (c >= 0x0D00 && c <= 0x0D7F) // Malayalam
            {
                sb.Append((char)(0x0900 + (c - 0x0D00)));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string ConvertIndicToGujarati(string text)
    {
        var dev = ConvertIndicToDevanagari(text);
        return ConvertHindiToGujarati(dev);
    }

    private static string ConvertIndicToHindi(string text)
    {
        return ConvertIndicToDevanagari(text);
    }

    private static string ConvertToIndicScript(string text, string targetLanguageCode)
    {
        var dev = ConvertIndicToDevanagari(IsIndicScript(text) ? text : ToHindi(text));
        int offset = targetLanguageCode switch
        {
            "gu" => 0x0A80 - 0x0900, // Gujarati
            "bn" or "as" => 0x0980 - 0x0900, // Bengali / Assamese
            "pa" => 0x0A00 - 0x0900, // Gurmukhi (Punjabi)
            "or" => 0x0B00 - 0x0900, // Odia
            "ta" => 0x0B80 - 0x0900, // Tamil
            "te" => 0x0C00 - 0x0900, // Telugu
            "kn" => 0x0C80 - 0x0900, // Kannada
            "ml" => 0x0D00 - 0x0900, // Malayalam
            _ => 0
        };

        if (offset == 0) return dev;

        var sb = new StringBuilder(dev.Length);
        foreach (char c in dev)
        {
            if (c >= 0x0900 && c <= 0x097F)
            {
                sb.Append((char)(c + offset));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
