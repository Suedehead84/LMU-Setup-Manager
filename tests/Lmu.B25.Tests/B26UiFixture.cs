static class B26UiFixture
{
    public static async Task Run()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"wwwroot","locales");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"fr.json");var previous=File.Exists(path)?File.ReadAllBytes(path):null;
        try{File.Copy("tests/fixtures/locales/fr.json",path,true);await B251UiFixture.Run("artifacts/b26-ui",5118);}
        finally{if(previous is not null)File.WriteAllBytes(path,previous);else if(File.Exists(path))File.Delete(path);}
    }
}
