using System;
using System.Collections.Generic;
using SnakeClub.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnakeClub
{
    // Runtime uGUI home: one dominant action and a live 3D character.
    public sealed class HomeScreen : MonoBehaviour
    {
        private readonly List<Graphic> motes = new List<Graphic>();
        private readonly List<Vector2> moteOrigins = new List<Vector2>();
        private readonly List<Graphic> resultSparks = new List<Graphic>();
        private readonly List<Vector2> resultSparkOrigins = new List<Vector2>();
        private Canvas canvas;
        private RectTransform safe, layout, homePage, skinPage, resultPage, resultCard, resultRetryRect, settingsModal;
        private RawImage background, portrait, skinPortrait, resultPortrait;
        private Text level, coins, notice, primaryText, primaryHint, caveText, soundText;
        private Text skinName, skinDescription, skinActionText, skinNotice, skinTrialText;
        private Text resultTitle, resultStats, resultDetails, resultAdText, settingsSound, settingsEffects, settingsHaptics, settingsMusic;
        private readonly Image[] skinCards = new Image[SkinCatalog.All.Length];
        private readonly RawImage[] skinThumbnails = new RawImage[SkinCatalog.All.Length];
        private readonly Text[] skinStates = new Text[SkinCatalog.All.Length];
        private Button primary, skinAction, skinTrial, resultRetry, resultShare, resultAd, reconnect;
        private Button skinPrevious, skinNext, hapticsButton;
        private Text skinPageLabel;
        private RectTransform primaryRect;
        private Texture2D roundTexture;
        private Sprite roundSprite;
        private Font font;
        private Action onStart, onSkins, onCaves, onMore, onSound;
        private Action onSkinBack, onSkinTrial;
        private Action onResultRetry, onResultMenu, onResultShare, onResultAd, onReconnect, onEffects, onHaptics, onMusic;
        private Action<int> onSkinSelect, onSkinEquip;
        private int selectedPreview, skinPageIndex;
        private Rect lastSafe;
        private Vector2Int lastSize;
        private float resultOpenedAt;
        private bool reducedEffects;

        public bool Visible
        {
            get => canvas != null && canvas.gameObject.activeSelf;
            set { if (canvas != null && canvas.gameObject.activeSelf != value) canvas.gameObject.SetActive(value); }
        }

        public void Setup(Font uiFont, Action start, Action skins, Action caves, Action more, Action sound,
            Action skinBack, Action<int> skinSelect, Action<int> skinEquip, Action skinTry)
        {
            font = uiFont; onStart = start; onSkins = skins; onCaves = caves; onMore = more; onSound = sound;
            onSkinBack = skinBack; onSkinSelect = skinSelect; onSkinEquip = skinEquip; onSkinTrial = skinTry;
            var prior = transform.Find("Polished Home UI");
            if (prior != null) { prior.gameObject.SetActive(false); Destroy(prior.gameObject); }
            motes.Clear(); moteOrigins.Clear(); resultSparks.Clear(); resultSparkOrigins.Clear();
            var root = new GameObject("Polished Home UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Home Event System", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(root.transform, false);
            }
            MakeRoundSprite();
            background = MakeRaw("Garden backdrop", root.transform, new Rect(0, 0, 1080, 1920), Resources.Load<Texture2D>("HomeGarden"));
            var bgRect = background.rectTransform; bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = bgRect.offsetMax = Vector2.zero; background.raycastTarget = false;
            safe = NewRect("Safe area", root.transform);
            safe.anchorMin = Vector2.zero; safe.anchorMax = Vector2.one; safe.offsetMin = safe.offsetMax = Vector2.zero;
            layout = NewRect("Portrait layout", safe);
            layout.anchorMin = layout.anchorMax = new Vector2(.5f, .5f); layout.pivot = new Vector2(.5f, .5f);
            layout.sizeDelta = new Vector2(1080, 1920); layout.anchoredPosition = Vector2.zero;

            Panel("Top shade", layout, new Rect(55, 45, 970, 115), new Color(.025f, .09f, .16f, .82f));
            level = MakeText("Level", layout, new Rect(95, 62, 430, 70), "等级 1", 29, Color.white, TextAnchor.MiddleLeft, true);
            coins = MakeText("Coins", layout, new Rect(580, 62, 390, 70), "金币 0", 29, new Color(1f, .88f, .54f), TextAnchor.MiddleRight, true);

            MakeText("Brand", layout, new Rect(140, 190, 800, 54), "S N A K E   C L U B", 26, new Color(.69f, 1f, .93f), TextAnchor.MiddleCenter, true);
            var title = MakeText("Title", layout, new Rect(100, 245, 880, 120), "灵蛇秘境", 82, Color.white, TextAnchor.MiddleCenter, true);
            var titleShadow = title.gameObject.AddComponent<Shadow>(); titleShadow.effectColor = new Color(.02f, .14f, .22f, .8f); titleShadow.effectDistance = new Vector2(0, -5);
            MakeText("Subtitle", layout, new Rect(130, 365, 820, 55), "围猎对手  ·  成为今日蛇王", 29, new Color(.83f, .99f, .95f), TextAnchor.MiddleCenter);

            portrait = MakeRaw("Live character", layout, new Rect(75, 375, 930, 930), null);
            portrait.raycastTarget = false;
            var characterButton = MakeButton("Character button", layout, new Rect(220, 500, 640, 600), new Color(1, 1, 1, 0), () => onSkins?.Invoke());
            characterButton.image.raycastTarget = true;
            MakeText("Skin invitation", layout, new Rect(230, 1090, 620, 52), "点击灵蛇更换造型", 24, new Color(.86f, 1f, .94f), TextAnchor.MiddleCenter);

            for (int i = 0; i < 16; i++)
            {
                float x = 120 + (i * 197 % 830), y = 465 + (i * 239 % 590);
                var mote = Panel("Floating light", layout, new Rect(x, y, 12 + i % 3 * 5, 12 + i % 3 * 5),
                    i % 2 == 0 ? new Color(1f, .94f, .66f, .85f) : new Color(.61f, 1f, .94f, .8f));
                mote.raycastTarget = false; motes.Add(mote); moteOrigins.Add(mote.rectTransform.anchoredPosition);
            }

            Panel("Primary shadow", layout, new Rect(112, 1230, 856, 153), new Color(.01f, .19f, .22f, .80f));
            primary = MakeButton("Start hunt", layout, new Rect(112, 1218, 856, 145), new Color(.08f, .72f, .56f, 1f), () => onStart?.Invoke());
            primaryRect = primary.GetComponent<RectTransform>();
            primaryText = MakeText("Primary label", primary.transform, new Rect(0, 13, 856, 74), "开始围猎", 45, Color.white, TextAnchor.MiddleCenter, true);
            primaryHint = MakeText("Primary hint", primary.transform, new Rect(0, 81, 856, 42), "今日挑战 · 同图 60 秒", 23, new Color(.83f, 1f, .93f), TextAnchor.MiddleCenter);

            Tile("装扮", "角色与皮肤", 80, () => onSkins?.Invoke(), new Color(.30f, .39f, .72f, .94f));
            Tile("探洞", "离线也有收获", 390, () => onCaves?.Invoke(), new Color(.14f, .50f, .51f, .94f));
            Tile("更多", "排行 · 天赋", 700, () => onMore?.Invoke(), new Color(.43f, .33f, .61f, .94f));

            Panel("Progress panel", layout, new Rect(95, 1640, 890, 120), new Color(.025f, .10f, .16f, .84f));
            caveText = MakeText("Cave progress", layout, new Rect(125, 1653, 830, 55), "派灵蛇去梦游探洞", 25, Color.white, TextAnchor.MiddleCenter);
            notice = MakeText("Connection", layout, new Rect(125, 1706, 830, 38), "", 20, new Color(.72f, .91f, .88f), TextAnchor.MiddleCenter);
            var soundButton = MakeButton("Sound", layout, new Rect(780, 1790, 200, 68), new Color(.04f, .16f, .21f, .8f), () => onSound?.Invoke());
            soundText = MakeText("Sound label", soundButton.transform, new Rect(0, 0, 200, 68), "音效：开", 22, Color.white, TextAnchor.MiddleCenter, true);
            reconnect = MakeButton("Reconnect", layout, new Rect(100, 1790, 240, 68), new Color(.08f, .35f, .34f, .9f), () => onReconnect?.Invoke());
            MakeText("Reconnect label", reconnect.transform, new Rect(0, 0, 240, 68), "重新连接", 22, Color.white, TextAnchor.MiddleCenter, true);
            var settings = MakeButton("Settings", layout, new Rect(410, 1790, 240, 68), new Color(.04f, .16f, .21f, .8f), () => settingsModal.gameObject.SetActive(true));
            MakeText("Settings label", settings.transform, new Rect(0, 0, 240, 68), "设置", 22, Color.white, TextAnchor.MiddleCenter, true);
            var homeChildren = new List<Transform>();
            foreach (Transform child in layout) homeChildren.Add(child);
            homePage = NewRect("Home page", layout); Position(homePage, new Rect(0, 0, 1080, 1920));
            foreach (var child in homeChildren) child.SetParent(homePage, false);
            BuildSettings();
            BuildSkinPage();
            BuildResultPage();
            lastSize = Vector2Int.zero; UpdateSafeArea();
        }

        public void SetupResults(Action retry, Action menu, Action share, Action ad)
        {
            onResultRetry = retry; onResultMenu = menu; onResultShare = share; onResultAd = ad;
        }

        public void SetupReconnect(Action reconnectAction) => onReconnect = reconnectAction;
        public void SetupPreferences(Action effects, Action haptics, Action music) { onEffects = effects; onHaptics = haptics; onMusic = music; }

        public void SetPage(bool skins, bool results = false)
        {
            if (homePage == null || skinPage == null || resultPage == null) return;
            bool showHome = !skins && !results, showSkins = skins && !results;
            if (homePage.gameObject.activeSelf != showHome) homePage.gameObject.SetActive(showHome);
            if (skinPage.gameObject.activeSelf != showSkins) skinPage.gameObject.SetActive(showSkins);
            if (resultPage.gameObject.activeSelf != results)
            {
                resultPage.gameObject.SetActive(results);
                if (results) resultOpenedAt = Time.unscaledTime;
            }
            if (background.enabled == results) background.enabled = !results;
        }

        public void SetState(PlayerProgress profile, Texture character, string message, bool busy, bool connected, bool sound,
            int equipped, int preview, bool referencesEnabled, bool trialAvailable, bool friendChallenge = false,
            bool reduce = false, bool haptics = false, bool hapticsSupported = false, bool music = true)
        {
            if (canvas == null || profile == null) return;
            reducedEffects = reduce;
            Set(level, "等级 " + Progression.Level(profile.experience));
            Set(coins, "金币  " + profile.coins);
            Set(primaryText, busy ? "正在准备…" : friendChallenge ? "接受好友挑战" : "开始围猎");
            Set(primaryHint, !connected ? "离线练习 · 不计排行榜" : !profile.tutorialComplete ? "新手练习 · 学会围猎" : "今日挑战 · 同图 60 秒");
            Set(caveText, connected && !profile.ownedSkins.Contains(1) && profile.completedMatches < 2 ?
                "完成 2 场，免费解锁曜焰  " + profile.completedMatches + "/2" :
                profile.expedition != null && !profile.expedition.claimed ? "梦游探洞进行中，回来可领取奖励" : "派灵蛇去梦游探洞，离线也有收获");
            Set(notice, message ?? "");
            Set(soundText, sound ? "音效：开" : "音效：关");
            Set(settingsSound, sound ? "音效：开" : "音效：关");
            Set(settingsMusic, music ? "音乐：开" : "音乐：关");
            hapticsButton.interactable = hapticsSupported;
            Set(settingsHaptics, !hapticsSupported ? "震动：当前平台不可用" : haptics ? "震动：开" : "震动：关");
            Set(settingsEffects, reducedEffects ? "画面特效：简洁" : "画面特效：丰富");
            primary.interactable = !busy;
            if (reconnect.gameObject.activeSelf == connected) reconnect.gameObject.SetActive(!connected);
            if (portrait.texture != character) portrait.texture = character;
            if (skinPortrait.texture != character) skinPortrait.texture = character;
            if (resultPortrait.texture != character) resultPortrait.texture = character;
            selectedPreview = Mathf.Clamp(preview, 0, SkinCatalog.All.Length - 1);
            var skin = SkinCatalog.Get(selectedPreview);
            Set(skinName, skin.Name);
            Set(skinDescription, skin.Description + (skin.ReferencePreview ? " · 内部参考" : " · 每日挑战属性统一"));
            for (int i = 0; i < skinCards.Length; i++)
            {
                var card = skinCards[i];
                bool show = (i < SkinCatalog.OwnedSkinCount || referencesEnabled) && i / 6 == skinPageIndex;
                if (card.gameObject.activeSelf != show) card.gameObject.SetActive(show);
                var thumbnail = skinThumbnails[i];
                if (show && thumbnail.texture == null)
                {
                    thumbnail.texture = SkinVisual.Texture(i);
                    if (thumbnail.texture != null)
                    {
                        float scale = Mathf.Min(90f / thumbnail.texture.width, 130f / thumbnail.texture.height);
                        float width = thumbnail.texture.width * scale, height = thumbnail.texture.height * scale;
                        Position(thumbnail.rectTransform, new Rect(70f - width / 2f, 78f - height / 2f, width, height));
                    }
                }
                thumbnail.enabled = show && thumbnail.texture != null;
                Color cardColor = i == selectedPreview ? new Color(.17f, .63f, .57f, .96f) : new Color(.035f, .14f, .21f, .94f);
                if (card.color != cardColor) card.color = cardColor;
                Set(skinStates[i], SkinCatalog.Get(i).ReferencePreview ? "造型预览" : profile.ownedSkins.Contains(i) ? "已拥有" :
                    i == 1 ? (connected ? "完成 2 场免费领取" : "联网完成 2 场领取") : SkinCatalog.Get(i).Price + " 金币");
            }
            int pages = referencesEnabled ? (SkinCatalog.All.Length + 5) / 6 : 1;
            if (skinPageIndex >= pages) skinPageIndex = pages - 1;
            skinPrevious.interactable = skinPageIndex > 0;
            skinNext.interactable = skinPageIndex + 1 < pages;
            Set(skinPageLabel, (skinPageIndex + 1) + " / " + pages);
            bool owned = profile.ownedSkins.Contains(selectedPreview);
            bool available = !skin.ReferencePreview || (referencesEnabled && SkinVisual.Texture(selectedPreview) != null);
            Set(skinActionText, !available ? "素材不可用" : equipped == selectedPreview ? "使用中" : skin.ReferencePreview ? "本机试用" : owned ? "使用造型" :
                skin.Price + (selectedPreview == 1 ? " 金币提前解锁" : " 金币解锁"));
            Set(skinNotice, string.IsNullOrEmpty(message) ? "金币 " + profile.coins : message);
            skinAction.interactable = available && !busy && equipped != selectedPreview;
            skinTrial.gameObject.SetActive(trialAvailable && !skin.ReferencePreview && !owned);
            Set(skinTrialText, "看 1 次广告，试穿 1 局");
        }

        private void BuildSettings()
        {
            settingsModal = NewRect("Settings modal", homePage); Position(settingsModal, new Rect(0, 0, 1080, 1920));
            var veil = Panel("Settings veil", settingsModal, new Rect(0, 0, 1080, 1920), new Color(.015f, .035f, .08f, .82f));
            veil.raycastTarget = true;
            Panel("Settings panel", settingsModal, new Rect(130, 390, 820, 1080), new Color(.035f, .13f, .19f, .98f));
            MakeText("Settings title", settingsModal, new Rect(180, 430, 720, 90), "游戏设置", 47, Color.white, TextAnchor.MiddleCenter, true);
            var sound = MakeButton("Settings sound", settingsModal, new Rect(220, 550, 640, 105), new Color(.12f, .35f, .40f, .98f), () => onSound?.Invoke());
            settingsSound = MakeText("Settings sound label", sound.transform, new Rect(0, 0, 640, 105), "音效：开", 28, Color.white, TextAnchor.MiddleCenter, true);
            var musicButton = MakeButton("Settings music", settingsModal, new Rect(220, 670, 640, 105), new Color(.12f, .35f, .40f, .98f), () => onMusic?.Invoke());
            settingsMusic = MakeText("Settings music label", musicButton.transform, new Rect(0, 0, 640, 105), "音乐：开", 28, Color.white, TextAnchor.MiddleCenter, true);
            hapticsButton = MakeButton("Settings haptics", settingsModal, new Rect(220, 790, 640, 105), new Color(.12f, .35f, .40f, .98f), () => onHaptics?.Invoke());
            settingsHaptics = MakeText("Settings haptics label", hapticsButton.transform, new Rect(0, 0, 640, 105), "震动：关", 28, Color.white, TextAnchor.MiddleCenter, true);
            MakeText("Settings controls label", settingsModal, new Rect(220, 1070, 640, 105), "控制：全屏拖动 · 双指加速", 28, Color.white, TextAnchor.MiddleCenter, true);
            var effects = MakeButton("Settings effects", settingsModal, new Rect(220, 910, 640, 105), new Color(.12f, .35f, .40f, .98f), () => onEffects?.Invoke());
            settingsEffects = MakeText("Settings effects label", effects.transform, new Rect(0, 0, 640, 105), "画面特效：丰富", 28, Color.white, TextAnchor.MiddleCenter, true);
            var close = MakeButton("Settings close", settingsModal, new Rect(300, 1250, 480, 100), new Color(.08f, .68f, .54f, 1f), () => settingsModal.gameObject.SetActive(false));
            MakeText("Settings close label", close.transform, new Rect(0, 0, 480, 100), "完成", 30, Color.white, TextAnchor.MiddleCenter, true);
            settingsModal.gameObject.SetActive(false);
        }

        public void SetResult(string title, string stats, string details, bool busy, bool canShare, int adCoins)
        {
            if (resultPage == null) return;
            Set(resultTitle, title); Set(resultStats, stats); Set(resultDetails, details ?? "");
            resultRetry.interactable = !busy;
            if (resultShare.gameObject.activeSelf != canShare) resultShare.gameObject.SetActive(canShare);
            bool canAd = adCoins > 0;
            if (resultAd.gameObject.activeSelf != canAd) resultAd.gameObject.SetActive(canAd);
            Set(resultAdText, "看广告再得 " + adCoins + " 金币");
        }

        private void BuildResultPage()
        {
            resultPage = NewRect("Result page", layout); Position(resultPage, new Rect(0, 0, 1080, 1920));
            var veil = Panel("Result veil", resultPage, new Rect(0, 0, 1080, 1920), new Color(.015f, .035f, .08f, .82f));
            veil.raycastTarget = true;
            resultCard = NewRect("Result card", resultPage); Position(resultCard, new Rect(75, 270, 930, 1370));
            Panel("Result glow", resultCard, new Rect(0, 0, 930, 1370), new Color(.045f, .15f, .22f, .96f));
            Panel("Result accent", resultCard, new Rect(95, 120, 740, 660), new Color(.11f, .42f, .43f, .28f));
            resultTitle = MakeText("Result title", resultCard, new Rect(90, 50, 750, 95), "围猎结束", 55, new Color(1f, .93f, .70f), TextAnchor.MiddleCenter, true);
            resultPortrait = MakeRaw("Result character", resultCard, new Rect(175, 135, 580, 580), null); resultPortrait.raycastTarget = false;
            resultStats = MakeText("Result stats", resultCard, new Rect(75, 665, 780, 120), "", 34, Color.white, TextAnchor.MiddleCenter, true);
            resultDetails = MakeText("Result details", resultCard, new Rect(95, 785, 740, 112), "", 24, new Color(.78f, .93f, .90f), TextAnchor.MiddleCenter);
            resultRetry = MakeButton("Result retry", resultCard, new Rect(125, 925, 680, 120), new Color(.08f, .72f, .56f, 1f), () => onResultRetry?.Invoke());
            resultRetryRect = resultRetry.GetComponent<RectTransform>();
            MakeText("Retry label", resultRetry.transform, new Rect(0, 0, 680, 120), "再来一局", 36, Color.white, TextAnchor.MiddleCenter, true);
            var menu = MakeButton("Result menu", resultCard, new Rect(125, 1065, 325, 100), new Color(.18f, .30f, .40f, .98f), () => onResultMenu?.Invoke());
            MakeText("Menu label", menu.transform, new Rect(0, 0, 325, 100), "返回大厅", 28, Color.white, TextAnchor.MiddleCenter, true);
            resultShare = MakeButton("Result share", resultCard, new Rect(480, 1065, 325, 100), new Color(.26f, .38f, .65f, .98f), () => onResultShare?.Invoke());
            MakeText("Share label", resultShare.transform, new Rect(0, 0, 325, 100), "挑战好友", 28, Color.white, TextAnchor.MiddleCenter, true);
            resultAd = MakeButton("Result ad", resultCard, new Rect(170, 1190, 590, 80), new Color(.32f, .42f, .53f, .96f), () => onResultAd?.Invoke());
            resultAdText = MakeText("Ad label", resultAd.transform, new Rect(0, 0, 590, 80), "", 23, Color.white, TextAnchor.MiddleCenter, true);
            for (int i = 0; i < 14; i++)
            {
                float x = 78 + (i * 179 % 770), y = 175 + (i * 163 % 520);
                var spark = Panel("Result sparkle", resultCard, new Rect(x, y, 14 + i % 3 * 6, 14 + i % 3 * 6),
                    i % 2 == 0 ? new Color(1f, .86f, .42f, .85f) : new Color(.57f, 1f, .87f, .8f));
                resultSparks.Add(spark); resultSparkOrigins.Add(spark.rectTransform.anchoredPosition);
            }
            resultPage.gameObject.SetActive(false);
        }

        private void BuildSkinPage()
        {
            skinPage = NewRect("Skin page", layout); Position(skinPage, new Rect(0, 0, 1080, 1920));
            Panel("Skin header", skinPage, new Rect(50, 45, 980, 175), new Color(.025f, .09f, .16f, .9f));
            var back = MakeButton("Skin back", skinPage, new Rect(75, 78, 145, 78), new Color(.18f, .30f, .40f, .98f), () => onSkinBack?.Invoke());
            MakeText("Back label", back.transform, new Rect(0, 0, 145, 78), "返回", 26, Color.white, TextAnchor.MiddleCenter, true);
            MakeText("Skin title", skinPage, new Rect(245, 72, 610, 78), "灵蛇衣橱", 47, Color.white, TextAnchor.MiddleCenter, true);
            MakeText("Skin subtitle", skinPage, new Rect(210, 158, 660, 45), "选择喜欢的角色，带它去围猎", 23, new Color(.75f, .94f, .90f), TextAnchor.MiddleCenter);
            skinPortrait = MakeRaw("Skin character", skinPage, new Rect(190, 205, 700, 700), null); skinPortrait.raycastTarget = false;
            skinName = MakeText("Skin name", skinPage, new Rect(120, 790, 840, 70), "", 39, Color.white, TextAnchor.MiddleCenter, true);
            skinDescription = MakeText("Skin description", skinPage, new Rect(120, 850, 840, 55), "", 23, new Color(.80f, .96f, .92f), TextAnchor.MiddleCenter);
            Color[] swatches = { new Color(.12f, .83f, .69f), new Color(1f, .43f, .20f), new Color(.44f, .39f, .91f),
                new Color(.23f, .78f, .93f), new Color(.93f, .95f, 1f), new Color(.88f, .18f, .22f),
                new Color(1f, .43f, .10f), new Color(.68f, .72f, .75f), new Color(1f, .9f, .12f),
                new Color(.12f, .8f, .4f), new Color(.48f, .49f, .52f), new Color(.95f, .27f, .7f) };
            for (int i = 0; i < skinCards.Length; i++)
            {
                int index = i;
                int slot = i % 6;
                var card = MakeButton("Skin card " + i, skinPage, new Rect(85 + slot % 2 * 465, 925 + slot / 2 * 175, 445, 155),
                    new Color(.035f, .14f, .21f, .94f), () => onSkinSelect?.Invoke(index));
                skinCards[i] = card.image;
                Panel("Color swatch", card.transform, new Rect(25, 34, 80, 95), swatches[i % swatches.Length]);
                var thumbnail = MakeRaw("Character thumbnail", card.transform, new Rect(25, 13, 90, 130), null);
                thumbnail.raycastTarget = false; thumbnail.enabled = false; skinThumbnails[i] = thumbnail;
                MakeText("Name", card.transform, new Rect(125, 25, 295, 58), SkinCatalog.Get(i).Name, 30, Color.white, TextAnchor.MiddleLeft, true);
                skinStates[i] = MakeText("Status", card.transform, new Rect(125, 90, 295, 48), "", 21, new Color(.76f, .92f, .90f), TextAnchor.MiddleLeft);
            }
            skinPrevious = MakeButton("Previous skins", skinPage, new Rect(220, 1455, 210, 55), new Color(.13f, .30f, .37f), () => { if (skinPageIndex > 0) skinPageIndex--; });
            MakeText("Previous label", skinPrevious.transform, new Rect(0, 0, 210, 55), "上一页", 22, Color.white, TextAnchor.MiddleCenter, true);
            skinNext = MakeButton("Next skins", skinPage, new Rect(650, 1455, 210, 55), new Color(.13f, .30f, .37f), () => { if ((skinPageIndex + 1) * 6 < SkinCatalog.All.Length) skinPageIndex++; });
            MakeText("Next label", skinNext.transform, new Rect(0, 0, 210, 55), "下一页", 22, Color.white, TextAnchor.MiddleCenter, true);
            skinPageLabel = MakeText("Skin page number", skinPage, new Rect(440, 1455, 200, 55), "1 / 2", 22, Color.white, TextAnchor.MiddleCenter);
            skinAction = MakeButton("Equip skin", skinPage, new Rect(155, 1525, 770, 118), new Color(.08f, .68f, .54f), () => onSkinEquip?.Invoke(selectedPreview));
            skinActionText = MakeText("Equip label", skinAction.transform, new Rect(0, 0, 770, 118), "使用造型", 34, Color.white, TextAnchor.MiddleCenter, true);
            skinTrial = MakeButton("Trial skin", skinPage, new Rect(250, 1655, 580, 76), new Color(.22f, .38f, .51f), () => onSkinTrial?.Invoke());
            skinTrialText = MakeText("Trial label", skinTrial.transform, new Rect(0, 0, 580, 76), "", 22, Color.white, TextAnchor.MiddleCenter, true);
            skinNotice = MakeText("Skin notice", skinPage, new Rect(100, 1750, 880, 60), "", 22, new Color(.87f, .98f, .94f), TextAnchor.MiddleCenter);
            skinPage.gameObject.SetActive(false);
        }

        private static void Set(Text label, string value) { if (label != null && label.text != value) label.text = value; }

        private void Tile(string title, string subtitle, float x, Action action, Color color)
        {
            var button = MakeButton(title, layout, new Rect(x, 1435, 300, 165), color, action);
            MakeText(title + " title", button.transform, new Rect(0, 22, 300, 58), title, 34, Color.white, TextAnchor.MiddleCenter, true);
            MakeText(title + " subtitle", button.transform, new Rect(0, 88, 300, 42), subtitle, 21, new Color(.84f, .95f, .96f), TextAnchor.MiddleCenter);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var item = new GameObject(name, typeof(RectTransform)); item.transform.SetParent(parent, false);
            return item.GetComponent<RectTransform>();
        }

        private static void Position(RectTransform target, Rect bounds)
        {
            target.anchorMin = target.anchorMax = new Vector2(0, 1); target.pivot = new Vector2(0, 1);
            target.anchoredPosition = new Vector2(bounds.x, -bounds.y); target.sizeDelta = new Vector2(bounds.width, bounds.height);
        }

        private RawImage MakeRaw(string name, Transform parent, Rect bounds, Texture texture)
        {
            var rect = NewRect(name, parent); Position(rect, bounds);
            var image = rect.gameObject.AddComponent<RawImage>(); image.texture = texture; return image;
        }

        private Image Panel(string name, Transform parent, Rect bounds, Color color)
        {
            var rect = NewRect(name, parent); Position(rect, bounds);
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = roundSprite; image.type = Image.Type.Sliced;
            image.color = color; image.raycastTarget = false; return image;
        }

        private Button MakeButton(string name, Transform parent, Rect bounds, Color color, Action action)
        {
            var image = Panel(name, parent, bounds, color); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, 1f, 1f, .94f);
            colors.pressedColor = new Color(.78f, .87f, .88f, 1f); colors.disabledColor = new Color(.70f, .70f, .70f, .8f); button.colors = colors;
            button.onClick.AddListener(() => action?.Invoke()); return button;
        }

        private Text MakeText(string name, Transform parent, Rect bounds, string value, int size, Color color, TextAnchor anchor, bool bold = false)
        {
            var rect = NewRect(name, parent); Position(rect, bounds);
            var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; text.color = color; text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false;
            return text;
        }

        private void MakeRoundSprite()
        {
            const int size = 96, corner = 32;
            roundTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Rounded UI", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x - 47.5f) - (47.5f - corner));
                float dy = Mathf.Max(0, Mathf.Abs(y - 47.5f) - (47.5f - corner));
                float alpha = Mathf.Clamp01(corner + .5f - Mathf.Sqrt(dx * dx + dy * dy));
                roundTexture.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
            roundTexture.Apply();
            roundSprite = Sprite.Create(roundTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(corner, corner, corner, corner));
        }

        private void UpdateSafeArea()
        {
            if (safe == null || Screen.width <= 0 || Screen.height <= 0) return;
            Rect bounds = Screen.safeArea;
            safe.anchorMin = new Vector2(bounds.xMin / Screen.width, bounds.yMin / Screen.height);
            safe.anchorMax = new Vector2(bounds.xMax / Screen.width, bounds.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            float scale = Mathf.Min(safe.rect.width / 1080f, safe.rect.height / 1920f);
            layout.localScale = Vector3.one * Mathf.Max(.01f, scale);
            if (background != null && background.texture != null)
            {
                float imageAspect = background.texture.width / (float)background.texture.height;
                float screenAspect = Screen.width / (float)Screen.height;
                if (screenAspect < imageAspect)
                {
                    float width = screenAspect / imageAspect; background.uvRect = new Rect((1f - width) * .5f, 0, width, 1);
                }
                else
                {
                    float height = imageAspect / screenAspect; background.uvRect = new Rect(0, (1f - height) * .5f, 1, height);
                }
            }
            lastSafe = bounds; lastSize = new Vector2Int(Screen.width, Screen.height);
        }

        private void Update()
        {
            if (!Visible) return;
            if (lastSize.x != Screen.width || lastSize.y != Screen.height || lastSafe != Screen.safeArea) UpdateSafeArea();
            float time = Time.unscaledTime;
            if (primaryRect != null)
            {
                float pulse = reducedEffects ? 1f : 1f + .012f * Mathf.Sin(time * 2.2f);
                primaryRect.localScale = new Vector3(pulse, pulse, 1);
            }
            if (resultPage != null && resultPage.gameObject.activeSelf)
            {
                float entrance = Mathf.Clamp01((time - resultOpenedAt) / .35f);
                float size = Mathf.Lerp(.90f, 1f, 1f - (1f - entrance) * (1f - entrance));
                resultCard.localScale = new Vector3(size, size, 1);
                float pulse = reducedEffects ? 1f : 1f + .015f * Mathf.Sin(time * 3f);
                resultRetryRect.localScale = new Vector3(pulse, pulse, 1);
                for (int i = 0; i < resultSparks.Count; i++)
                {
                    var spark = resultSparks[i];
                    if (spark.gameObject.activeSelf == reducedEffects) spark.gameObject.SetActive(!reducedEffects);
                    if (reducedEffects) continue;
                    var color = spark.color; color.a = .35f + .55f * (.5f + .5f * Mathf.Sin(time * 3f + i)); spark.color = color;
                    spark.rectTransform.anchoredPosition = resultSparkOrigins[i] + new Vector2(Mathf.Sin(time + i) * 14f, Mathf.Cos(time * 1.4f + i) * 19f);
                    spark.rectTransform.localRotation = Quaternion.Euler(0, 0, time * (20 + i * 2));
                }
            }
            for (int i = 0; i < motes.Count; i++)
            {
                var mote = motes[i]; if (mote == null) continue;
                if (mote.gameObject.activeSelf == reducedEffects) mote.gameObject.SetActive(!reducedEffects);
                if (reducedEffects) continue;
                float alpha = .35f + .5f * (.5f + .5f * Mathf.Sin(time * 2f + i));
                var color = mote.color; color.a = alpha; mote.color = color;
                mote.rectTransform.localRotation = Quaternion.Euler(0, 0, time * (8f + i));
                mote.rectTransform.anchoredPosition = moteOrigins[i] + new Vector2(Mathf.Sin(time * .7f + i) * 12f, Mathf.Cos(time * .9f + i) * 17f);
            }
        }

        private void OnDestroy()
        {
            if (roundSprite != null) { if (Application.isPlaying) Destroy(roundSprite); else DestroyImmediate(roundSprite); }
            if (roundTexture != null) { if (Application.isPlaying) Destroy(roundTexture); else DestroyImmediate(roundTexture); }
        }
    }
}
