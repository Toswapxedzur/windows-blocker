#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <aclapi.h>
#include <sddl.h>
#include <wincrypt.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <vector>
#include <array>
#include <cstring>
#include <string>
#include "CVaultWindows.h"

static void require(bool value, const char *message) {
    if (!value) throw std::runtime_error(message);
}

int main(int argc, char **argv) {
    try {
        if (argc != 2) throw std::runtime_error("Pass an isolated test directory.");
        std::filesystem::path root(argv[1]);
        std::filesystem::create_directories(root);
        std::array<uint8_t,32> random{};
        require(vault_windows_random(random.data(), random.size()) == 0, "randomness failed");
        require(random != std::array<uint8_t,32>{}, "randomness was zero");
        uint8_t *protectedBytes = nullptr, *decoded = nullptr;
        size_t protectedCount = 0, decodedCount = 0;
        require(vault_windows_protect(random.data(), random.size(), &protectedBytes, &protectedCount, 0) == 0, "DPAPI protect failed");
        require(protectedCount > random.size(), "DPAPI output was plaintext");
        require(vault_windows_protect(protectedBytes, protectedCount, &decoded, &decodedCount, 1) == 0, "DPAPI unprotect failed");
        require(decodedCount == random.size() && !memcmp(decoded,random.data(),random.size()), "DPAPI roundtrip mismatch");
        vault_windows_free(protectedBytes); vault_windows_free(decoded);
        auto privacyError = vault_windows_restrict_path(root.string().c_str());
        if (privacyError) throw std::runtime_error("private ACL failed: " + std::to_string(privacyError));
        PSECURITY_DESCRIPTOR descriptor = nullptr;
        require(GetNamedSecurityInfoW(root.wstring().data(), SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, nullptr, nullptr, nullptr, nullptr, &descriptor) == ERROR_SUCCESS, "ACL read failed");
        SECURITY_DESCRIPTOR_CONTROL control{}; DWORD revision = 0;
        require(GetSecurityDescriptorControl(descriptor,&control,&revision), "ACL control failed");
        require(control & SE_DACL_PROTECTED, "ACL inherited other users");
        BOOL aclPresent = FALSE, aclDefaulted = FALSE; PACL acl = nullptr;
        require(GetSecurityDescriptorDacl(descriptor,&aclPresent,&acl,&aclDefaulted) && aclPresent && acl,"private ACL missing");
        require(acl->AceCount == 2,"private ACL included unexpected principals");
        LocalFree(descriptor);
        auto longDirectory = root;
        for (int depth = 0; depth < 8; ++depth) longDirectory /= "private-path-with-a-deliberately-long-component";
        auto longNative = std::filesystem::path(L"\\\\?\\" + longDirectory.wstring());
        std::filesystem::create_directories(longNative);
        require(vault_windows_restrict_path(longDirectory.string().c_str()) == 0,"long private path failed");
        require(vault_windows_restrict_path(("/" + root.generic_string()).c_str()) == 0,"Foundation drive URL path failed");
        auto packages = root / "packages";
        auto release = packages / "release-1-test";
        std::filesystem::create_directories(release);
        std::ofstream(release / "seed-package.json") << "{}";
        std::ofstream(release / "signed-manifest.json") << "{}";
        vault_windows_prune_package(packages.string().c_str(),"release-1-test");
        require(!std::filesystem::exists(release),"safe package was not reclaimed");
        std::filesystem::create_directories(release);
        std::ofstream(release / "seed-package.json") << "{}";
        std::ofstream(release / "signed-manifest.json") << "{}";
        std::ofstream(release / "unexpected.txt") << "keep";
        vault_windows_prune_package(packages.string().c_str(),"release-1-test");
        require(std::filesystem::exists(release / "unexpected.txt"),"unexpected directory was reclaimed");
        auto linked = packages / "release-2-linked";
        std::filesystem::create_directories(linked);
        auto outside = root / "must-retain.json";
        std::ofstream(outside) << "private";
        std::filesystem::create_hard_link(outside,linked / "seed-package.json");
        std::ofstream(linked / "signed-manifest.json") << "{}";
        vault_windows_prune_package(packages.string().c_str(),"release-2-linked");
        require(std::filesystem::exists(linked / "seed-package.json") && std::filesystem::exists(outside),"hard-linked package was reclaimed");
        const char *png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4XkAAAAASUVORK5CYII=";
        DWORD imageCount = 0;
        require(CryptStringToBinaryA(png,0,CRYPT_STRING_BASE64,nullptr,&imageCount,nullptr,nullptr),"fixture decode failed");
        std::vector<uint8_t> image(imageCount);
        require(CryptStringToBinaryA(png,0,CRYPT_STRING_BASE64,image.data(),&imageCount,nullptr,nullptr),"fixture decode failed");
        uint8_t *jpeg = nullptr; size_t jpegCount = 0;
        require(vault_windows_image_jpeg(image.data(),image.size(),&jpeg,&jpegCount) == 0,"icon conversion failed");
        require(jpegCount > 2 && jpeg[0] == 0xff && jpeg[1] == 0xd8,"icon was not JPEG");
        vault_windows_free(jpeg);
        std::cout << "Windows native Classifier privacy/image/package checks passed\n";
        return 0;
    } catch (const std::exception &error) {
        std::cerr << error.what() << "\n";
        return 1;
    }
}
