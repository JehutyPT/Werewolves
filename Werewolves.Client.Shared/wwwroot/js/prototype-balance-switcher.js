// PROTOTYPE ONLY: global keyboard control for the balance-summary variant switcher.
export function attach(dotNetReference) {
  const handleKeyDown = (event) => {
    const target = event.target;
    const isEditing = target instanceof HTMLElement && (
      target.matches("input, textarea, select") || target.isContentEditable
    );

    if (isEditing || (event.key !== "ArrowLeft" && event.key !== "ArrowRight")) {
      return;
    }

    event.preventDefault();
    dotNetReference.invokeMethodAsync(
      "CycleFromKeyboard",
      event.key === "ArrowLeft" ? -1 : 1
    );
  };

  document.addEventListener("keydown", handleKeyDown);
  return {
    dispose() {
      document.removeEventListener("keydown", handleKeyDown);
    }
  };
}
