import Foundation

public enum AccountReader {
    private struct File: Decodable { let oauthAccount: Inner? }
    private struct Inner: Decodable {
        let accountUuid: String?
        let emailAddress: String?
        let organizationName: String?
    }

    /// Decodes only these keys, so tokens and project data are never held as values.
    public static func readAccount(configFile: URL) -> Account? {
        guard let data = try? Data(contentsOf: configFile),
              let file = try? JSONDecoder().decode(File.self, from: data),
              let a = file.oauthAccount, let uuid = a.accountUuid else { return nil }
        return Account(accountUuid: uuid, email: a.emailAddress, organizationName: a.organizationName)
    }
}
