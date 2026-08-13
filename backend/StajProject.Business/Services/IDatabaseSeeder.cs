namespace StajProject.Business.Services;

/// <summary>
/// Uygulama ilk açıldığında gerekli başlangıç verisini oluşturur.
///
/// Bu iş neden Business katmanında? Çünkü "hiç kullanıcı yoksa admin oluştur,
/// şifresini hash'le" bir İŞ KURALIDIR. Önceden Program.cs içindeydi; orada
/// olması sunum katmanının iş mantığı taşıması demekti (katman ihlali).
/// </summary>
public interface IDatabaseSeeder
{
    Task SeedAsync();
}
