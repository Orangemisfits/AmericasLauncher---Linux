[English](README.md) | [Türkçe](README.tr.md) | [Русский](README.ru.md) | [中文](README.zh.md) | [Español](README.es.md) | [Italiano](README.it.md) | [Polski](README.pl.md) | [Português (BR)](README.pt-BR.md) | **हिन्दी**

# Americas Launcher

एक आधुनिक Windows लॉन्चर जो Windows पर Xbox Series X|S (GDK) कम्पैटिबिलिटी लेयर्स को अपने आप सेटअप, अपडेट, बिल्ड और लॉन्च करता है — लाइव पोर्ट प्रोग्रेस ट्रैकिंग के साथ।

> यह प्रोजेक्ट **केवल उन गेम्स के लिए है जिनके आप कानूनी मालिक हैं**, जो आपके अपने PC पर निकाले गए हैं। Americas Launcher कोई भी गेम, कीज़ या कंसोल SDK फ़ाइलें **शामिल, डाउनलोड या वितरित नहीं** करता है।

![Americas Launcher - Launch View](docs/images/launch-view.png)

## मुख्य विशेषताएँ

- **आधुनिक 720p UI:** 9 चयन करने योग्य रनर कार्ड के साथ डार्क इंटरफ़ेस।
- **एक-क्लिक इंस्टॉल / अपडेट:** `winget` (Git, Visual Studio C++ Build Tools, Rust) के ज़रिए निर्भरताएँ जाँचता और इंस्टॉल करता है, और रनर को लोकली बिल्ड करता है।
- **एकीकृत कंसोल आउटपुट:** रनर आउटपुट और शेडर/पाइपलाइन संकलन लॉग सीधे लॉन्चर में दिखते हैं।
- **Stop Game:** लॉन्च बटन **Stop Game** में बदल जाता है ताकि आप कभी भी गेम या संकलन रोक सकें।
- **प्रति-रनर सेटिंग्स:** प्रत्येक रनर के लिए अलग **Pipeline**, **Xbox Series X/S**, और **रिज़ॉल्यूशन** (`540p`, `720p`, `1080p`, `2K`, `4K`) विकल्प।
- **लाइव पोर्ट स्थिति:** [Portwatch VI](https://portwatch-vi.xyz/) द्वारा लाइव प्रोग्रेस ट्रैकिंग।

## समर्थित रनर

| रनर | आधारित | किसके लिए बेहतर | रिपॉजिटरी |
|---|---|---|---|
| **XCL** | स्वतंत्र Xbox Series कम्पैटिबिलिटी लेयर | Both | [xcl-dev/xcl](https://github.com/xcl-dev/xcl) |
| **Belabug - VIctory Based** | VIctory फ़ॉर्क | AMD | [Belobug/VIctory-Belobug-AMD](https://github.com/Belobug/VIctory-Belobug-AMD) |
| **Dexnom - VIctory Based** | VIctory फ़ॉर्क | Both | [DexNom/VIctory](https://github.com/DexNom/VIctory) |
| **KLBF-LAB** | VIctory फ़ॉर्क | AMD | [PastiiJay/klbf-lab](https://github.com/PastiiJay/klbf-lab) |
| **VIctory(Original)** | मूल VIctory कम्पैटिबिलिटी लेयर | Unknown | [Marvisak/VIctory](https://github.com/Marvisak/VIctory) |
| **hyphenn - VIctory Based** | VIctory फ़ॉर्क | Unknown | [fxceless/victory-fork](https://github.com/fxceless/victory-fork) |
| **AvatarManz - VIctory Based** | VIctory फ़ॉर्क | AMD | [avatarmanz/VIctory-7900XTX](https://github.com/avatarmanz/VIctory-7900XTX) |
| **Nvidia - VIctory Based** | VIctory फ़ॉर्क (AvatarManz पर आधारित) | Nvidia | [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) |
| **CGBJ - Victory Based** | VIctory फ़ॉर्क | Nvidia | [DoubleGhosty/cool_fox](https://github.com/DoubleGhosty/cool_fox) |

## उपयोग

1. **Settings** खोलें और **Game Folder** तथा **Game Executable** (`Game.exe`) चुनें।
2. **Launch** टैब पर जाएँ और अपना रनर चुनें।
3. निर्भरताएँ इंस्टॉल करने और बिल्ड करने के लिए **Install / Update** पर क्लिक करें।
4. **Launch Game** पर क्लिक करें।

## लाइसेंस

[GNU GPL v2](LICENSE) (`GPL-2.0-only`).
