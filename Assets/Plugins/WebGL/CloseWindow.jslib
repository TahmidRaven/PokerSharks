mergeInto(LibraryManager.library, {
  // Close the browser tab/window when the player quits.
  // Note: browsers only allow window.close() on windows opened by script; for a normal tab the
  // close may be blocked, so we also blank the page as a fallback so the game clearly "exits".
  CloseGameWindow: function () {
    try { window.close(); } catch (e) {}
    try {
      window.open('', '_self');
      window.close();
    } catch (e) {}
    try { document.body.innerHTML = '<div style="color:#ddd;font:600 24px sans-serif;display:flex;height:100vh;align-items:center;justify-content:center;background:#0a0a0a;">Thanks for playing.</div>'; } catch (e) {}
  }
});
