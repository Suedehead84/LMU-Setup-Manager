using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Lmu.Localization;
using Lmu.Manager;
using Lmu.SetupLibrary;
using Lmu.Web;

static class B26Tests
{
    public static async Task Run(Action<bool,string> check)
    {
        var root=Path.GetFullPath(Path.Combine("artifacts","b26-tests-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
        var catalog=UiText.Catalog;
        check(catalog.Languages.Any(l=>l.Code=="de"&&l.Complete)&&catalog.Languages.Any(l=>l.Code=="en"&&l.Complete),"native catalog shares complete German and English resources");
        check(catalog.Resolve("de-DE")=="de"&&catalog.Resolve("en-GB")=="en","native language matching resolves regions");
        check(catalog.Resolve("zz-ZZ")=="en","unknown Windows language falls back to English");
        check(UiText.T("manager.openWebInterface","de")=="Weboberfläche öffnen"&&UiText.T("manager.openWebInterface","en")=="Open web interface","tray captions are localized from shared keys");
        check(UiText.T("manager.enableHomeNetworkAccess","en")=="Enable home network access"&&UiText.T("manager.disableHomeNetworkAccess","en")=="Disable home network access","both tray LAN actions are localized");
        check(UiText.T("manager.showServerStatus","en")=="Show server status"&&UiText.T("manager.quitLmuSetupManager","en")=="Quit LMU Setup Manager","tray status and quit actions are localized");
        check(TrayText.Tooltip(true,false,"de").Contains("nur lokal")&&TrayText.Tooltip(true,false,"en").Contains("local only"),"actual tooltip formatter follows explicit tray language");
        check(TrayText.Tooltip(false,false,"en").Contains("unavailable"),"stopped-server tooltip is localized");
        check(TrayText.FitTooltip(new string('x',90)).Length==63,"community tooltip length cannot exceed Windows limit");
        var longEmoji=new string('x',61)+"🏁"+new string('y',10);var fit=TrayText.FitTooltip(longEmoji);check(fit.Length<=63&&!char.IsHighSurrogate(fit[^2]),"tooltip truncation cannot split a Unicode surrogate pair");
        var canonical=UiText.C("manager.serverRunningLocallyOnly");check(canonical=="Server läuft ausschließlich lokal.","canonical diagnostic and IPC message contract stays unchanged");
        check(UiText.Localize(canonical,"en")=="Server running locally only.","stored canonical server status renders in requested language");
        check(UiText.C("diagnostics.scoringTooOld")=="Native scoring publication age > 2s"&&UiText.Localize(UiText.C("diagnostics.scoringTooOld"),"de").Contains("älter als 2 s"),"original English wire messages remain stable while German UI can localize them");
        var fieldError=UiText.C("library.textWith1CharactersRequired",UiText.C("library.modelName"),256);
        check(UiText.Localize(fieldError,"en")=="Model name: text with 1–256 characters required","dynamic validation translates system field label without translating input");
        check(UiText.Localize("SYNTHETIC Fahrzeug-ID unchanged","en")=="SYNTHETIC Fahrzeug-ID unchanged","unknown external system text is not partially rewritten");
        check(UiText.C("diagnostics.unsupportedAbi",99999).Contains("erwartet 14000 oder 14200")&&UiText.Localize(UiText.C("diagnostics.unsupportedAbi",99999),"en").Contains("expected 14000 or 14200"),"unsupported ABI warning localizes without modifying ABI validation");
        using(UiText.UseRequestLanguage("en")){check(UiText.RequestLanguage=="en","request locale is scoped");using(UiText.UseRequestLanguage("de"))check(UiText.RequestLanguage=="de","nested locale scope overrides only its own context");check(UiText.RequestLanguage=="en","nested locale scope restores caller");}
        check(UiText.RequestLanguage=="canonical","request locale does not leak into background canonical contracts");
        var languages=Path.Combine(root,"locales");Directory.CreateDirectory(languages);File.Copy("tests/fixtures/locales/fr.json",Path.Combine(languages,"fr.json"));
        var community=new LanguageCatalog(languages);check(community.Languages.Any(l=>l.Code=="fr"&&!l.Complete&&l.Translated==2),"new community file is discovered and marked partial without source changes");
        check(community.Format("web.readyForTheSession","fr")=="Prêt pour la session 🏁.","native resource reader preserves Unicode and accents");
        check(community.Format("web.notes","fr")=="Notes","native missing community key falls back to English");
        check(community.Format("settings.homeHttpConsent","fr")==UiText.T("settings.homeHttpConsent","en"),"partial native catalog uses complete English security bundle");
        var english=LanguageCatalog.Parse(File.ReadAllText("src/Lmu.Web/wwwroot/locales/en.json"));
        var partial=File.ReadAllText("tests/fixtures/locales/fr.json");
        void Reject(string json,string name){bool rejected=false;try{var doc=LanguageCatalog.Parse(json);LanguageCatalog.Validate(doc,english.Messages);}catch(Exception e)when(e is InvalidDataException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException){rejected=true;}check(rejected,name);}
        Reject("{\"meta\":{},\"meta\":{},\"messages\":{}}","native duplicate JSON properties rejected");
        Reject(partial.Replace("\"web.readyForTheSession\":","\"web.readyForTheSession\":\"duplicate\",\"web.readyForTheSession\":"),"native duplicate message key rejected");
        Reject(partial.Replace("Prêt pour la session 🏁.","<script>alert(1)</script>"),"native script translation rejected");
        Reject(partial.Replace("Prêt pour la session 🏁.","<b>Ready</b>"),"native HTML translation rejected");
        Reject(partial.Replace("Prêt pour la session 🏁.",""),"native empty translation rejected");
        Reject(partial.Replace("Prêt pour la session 🏁.","{bad}"),"native inconsistent placeholder rejected");
        Reject(partial.Replace("Prêt pour la session 🏁.","{value0:N2}"),"native unknown formatting directive rejected");
        Reject(partial.Replace("web.readyForTheSession","web.unusedKey"),"native unknown community key rejected");
        File.WriteAllText(Path.Combine(languages,"fr.json"),partial.Replace("\"partial\"","\"complete\""));check(!new LanguageCatalog(languages).Languages.Single(l=>l.Code=="fr").Complete,"new missing keys automatically mark a formerly complete community language partial");
        Reject(partial.Replace("\"code\":\"fr\"","\"code\":\"../fr\""),"native locale path traversal rejected");
        Reject(partial.Replace("Prêt pour la session 🏁.","Bad � text"),"native corrupted Unicode replacement rejected");
        Reject(partial.Replace("\"web.readyForTheSession\":", "\"common.restorePhrase\":"),"native community file cannot translate restore confirmation token");
        File.WriteAllText(Path.Combine(languages,"bad.json"),"{broken");File.WriteAllText(Path.Combine(languages,"it.json"),partial);
        var invalid=new LanguageCatalog(languages);check(invalid.RejectedFiles.Length==2&&invalid.Resolve("it")=="en","invalid or misnamed language files cannot enter runtime registry");
        var dbPath=Path.Combine(root,"library.sqlite");var library=new SetupLibraryService(new LibraryDatabase(dbPath));library.RegisterVehicle(new("SYNTHETIC-CAR","Fahrzeug nicht registriert"));library.RegisterTrack(new("SYNTHETIC-TRACK","Strecke nicht registriert"));library.RegisterLayout(new("SYNTHETIC-TRACK","GP","Papierkorb"));var saved=library.CreateManual(new("SYNTHETIC-CAR","SYNTHETIC-TRACK","GP"),new("Server läuft ausschließlich lokal.",new(5,6,7,6,49.5),Notes:"Keine aktuellen Werte <literal> 🏁",PreferredUse:"Papierkorb"));
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var networks=new Networks();var selected=networks.Read().Single();var store=new SettingsStore(Path.Combine(root,"config"));store.Save(new(false,false,port,true,new(selected.Fingerprint,port,true,true,true)));
        await using var manager=new ManagerController(store,dbPath,networks,new AutoStart());await manager.StartAsync();
        using var owner=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=false}){BaseAddress=new("http://127.0.0.1:"+port)};
        var key=new Uri(manager.LocalUrl()).Fragment.Split("access=")[1].Split('&')[0];owner.DefaultRequestHeaders.Authorization=new("Bearer",key);owner.DefaultRequestHeaders.Add("X-LMU-Language","en");
        using var anon=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=false}){BaseAddress=owner.BaseAddress};anon.DefaultRequestHeaders.Add("X-LMU-Language","en");
        var registry=await anon.GetFromJsonAsync<JsonElement>("/api/languages");check(registry.GetProperty("languages").EnumerateArray().Any(l=>l.GetProperty("code").GetString()=="de"),"language registry is available before pairing without exposing library data");
        var document=await anon.GetFromJsonAsync<JsonElement>("/api/languages/en");check(document.GetProperty("meta").GetProperty("code").GetString()=="en"&&document.GetProperty("messages").GetProperty("settings.homeHttpConsent").GetString()==UiText.T("settings.homeHttpConsent","en"),"public locale document contains validated translation text only");
        check((await anon.PostAsJsonAsync("/api/languages/en",new{})).StatusCode==HttpStatusCode.Unauthorized,"public language metadata exemption never permits unauthenticated writes");
        check((await anon.GetAsync("/api/languages/not-installed")).StatusCode==HttpStatusCode.NotFound,"unknown language document cannot resolve arbitrary files");
        var blocked=await anon.GetAsync("/api/setups");check(blocked.StatusCode==HttpStatusCode.Unauthorized&&(await blocked.Content.ReadAsStringAsync()).Contains("Connect to the library"),"unauthorized API remains blocked with localized security error");
        anon.DefaultRequestHeaders.Remove("X-LMU-Language");check((await (await anon.GetAsync("/api/setups?uiLanguage=en")).Content.ReadAsStringAsync()).Contains("Connect to the library"),"direct GET and download errors can follow per-browser language query without auth bypass");
        var view=await owner.GetFromJsonAsync<JsonElement>("/api/manager");check(view.GetProperty("message").GetString()!.Contains("trusted home network"),"canonical manager status is localized at JSON serialization boundary");
        check(view.GetProperty("networkSetup").GetProperty("choices")[0].GetProperty("profile").GetString()=="Public","Windows profile display localizes without changing Windows profile");
        var result=await owner.GetFromJsonAsync<JsonElement>("/api/setups/"+saved.Id);var content=result.GetProperty("current").GetProperty("content");
        check(content.GetProperty("name").GetString()=="Server läuft ausschließlich lokal."&&content.GetProperty("notes").GetString()=="Keine aktuellen Werte <literal> 🏁"&&content.GetProperty("preferredUse").GetString()=="Papierkorb","names notes profiles and Unicode are never translated even when matching application text");
        var cat=await owner.GetFromJsonAsync<JsonElement>("/api/catalog");check(cat.GetProperty("vehicles")[0].GetProperty("name").GetString()=="Fahrzeug nicht registriert"&&cat.GetProperty("layouts")[0].GetProperty("name").GetString()=="Papierkorb","original catalog display names and IDs remain opaque");
        var host=typeof(ManagerController).GetField("host",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(manager);
        var invite=JsonSerializer.SerializeToElement(manager.Invite("SYNTHETIC independent device"));var token=new Uri(invite.GetProperty("Urls")[0].GetString()!).Fragment[8..];var grant=manager.RedeemInvitation(token)!;
        manager.RequestSettings(manager.View().Settings with{TrayLanguage="en"});while(manager.View().Changing)await Task.Delay(20);
        check(ReferenceEquals(host,typeof(ManagerController).GetField("host",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(manager))&&manager.AuthenticateDevice(grant),"tray language change neither restarts server nor revokes existing device grant");
        check(store.Load().TrayLanguage=="en"&&manager.TrayLanguage=="en","tray language persists independently of browser preference");
        var session=await owner.GetFromJsonAsync<JsonElement>("/api/session");owner.DefaultRequestHeaders.Add("X-LMU-CSRF",session.GetProperty("csrf").GetString());
        var invalidSettings=await owner.PostAsJsonAsync("/api/manager/settings",manager.View().Settings with{TrayLanguage="not-installed"});check(invalidSettings.StatusCode==HttpStatusCode.BadRequest&&(await invalidSettings.Content.ReadAsStringAsync()).Contains("Unknown Windows application language"),"unknown explicit tray locale is rejected without weakening settings validation");
        using var device=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=false}){BaseAddress=owner.BaseAddress};device.DefaultRequestHeaders.Authorization=new("Bearer",grant);device.DefaultRequestHeaders.Add("X-LMU-Language","de");
        check((await device.GetAsync("/api/manager")).StatusCode==HttpStatusCode.Forbidden&&(await device.GetAsync("/api/setups")).IsSuccessStatusCode,"device language preference never grants owner administration");
        using var germanClient=new HttpClient(new HttpClientHandler{UseProxy=false,UseCookies=false}){BaseAddress=owner.BaseAddress};germanClient.DefaultRequestHeaders.Authorization=new("Bearer",key);germanClient.DefaultRequestHeaders.Add("X-LMU-Language","de");
        var both=await Task.WhenAll(owner.GetFromJsonAsync<JsonElement>("/api/manager"),germanClient.GetFromJsonAsync<JsonElement>("/api/manager"));check(both[0].GetProperty("message").GetString()!.Contains("trusted home network")&&both[1].GetProperty("message").GetString()!.Contains("privaten Heimnetz"),"concurrent clients get independent languages without changing global culture");
        check(library.Get(saved.Id).Current.Content.Name=="Server läuft ausschließlich lokal."&&library.Get(saved.Id).Versions.Count==1,"localization creates no setup revisions or persisted content changes");
    }
    private sealed class Networks:IPrivateNetworks{public PrivateInterface[] Read()=>[new("SYNTHETIC-LANGUAGE",IPAddress.Parse("127.0.0.2"),8,"SYNTHETIC language adapter","55555555-5555-5555-5555-555555555555",0,"127.0.0.1","SYNTHETIC","Ethernet",true)];}
    private sealed class AutoStart:IAutoStart{public bool Enabled=>false;public void Set(bool enabled){}}
}
