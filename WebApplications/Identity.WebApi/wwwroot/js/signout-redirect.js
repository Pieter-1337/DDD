// Auto-redirect after sign-out
window.addEventListener("load", function () {
    var redirectUri = document.getElementById("post-logout-redirect-uri");
    if (redirectUri) {
        window.location.href = redirectUri.value;
    }
});
